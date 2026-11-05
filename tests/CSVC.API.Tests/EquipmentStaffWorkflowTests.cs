using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CSVC.Core.Accounts;
using CSVC.Core.Departments;
using CSVC.Core.DeviceCategories;
using CSVC.Core.Devices;
using CSVC.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CSVC.API.Tests;

public sealed class EquipmentStaffWorkflowTests
{
    private const string Loans = "/api/v1/device-loans";
    private const string Transfers = "/api/v1/device-transfers";

    [PostgreSqlFact]
    public async Task Equipment_staff_can_create_edit_and_cancel_own_loans_and_transfers()
    {
        await using var f = await Fixture.CreateAsync();
        var loan = await f.CreateLoan(f.Staff);
        Assert.Equal("TK_STAFF", loan.GetProperty("borrowerAccountCode").GetString());
        Assert.Equal("PB_TEST", loan.GetProperty("departmentCode").GetString());
        var loanRoute = Loans + "/" + loan.GetProperty("loanCode").GetString();
        await Expect(await f.Staff.PutAsJsonAsync(loanRoute, f.LoanBody()), HttpStatusCode.NoContent);
        await Expect(await f.Department.PostAsJsonAsync(loanRoute + "/cancel", new { version = 2 }), HttpStatusCode.Forbidden);
        await Expect(await f.Staff.PostAsJsonAsync(loanRoute + "/cancel", new { version = 2 }), HttpStatusCode.NoContent);
        var transfer = await f.CreateTransfer();
        var transferRoute = Transfers + "/" + transfer.GetProperty("transferCode").GetString();
        await Expect(await f.Staff.PutAsJsonAsync(transferRoute, f.TransferBody()), HttpStatusCode.NoContent);
        await Expect(await f.Department.PostAsync(transferRoute + "/cancel", null), HttpStatusCode.Forbidden);
        await Expect(await f.Staff.PostAsync(transferRoute + "/cancel", null), HttpStatusCode.NoContent);
        await f.InDb(async db =>
        {
            Assert.Empty(await db.DeviceOperationHolds.ToListAsync());
            Assert.Equal(3, await db.DeviceLoanHistories.CountAsync());
            Assert.Equal(3, await db.DeviceTransferHistories.CountAsync());
        });
    }

    [PostgreSqlFact]
    public async Task Equipment_staff_acts_only_for_its_department_in_two_party_loans()
    {
        await using var f = await Fixture.CreateAsync();
        var loan = await f.CreateLoan(f.Staff);
        var route = Loans + "/" + loan.GetProperty("loanCode").GetString();
        await Expect(await f.Manager.PostAsJsonAsync(route + "/approve", new { version = 1 }), HttpStatusCode.Forbidden);
        await Expect(await f.Staff.PostAsJsonAsync(route + "/approve", new { version = 1 }), HttpStatusCode.Forbidden);
        await Expect(await f.Department.PostAsJsonAsync(route + "/approve", new { version = 1 }), HttpStatusCode.NoContent);
        await Expect(await f.ForeignStaff.PostAsJsonAsync(route + "/handover-complete", f.LoanConditions(2)), HttpStatusCode.Forbidden);
        await Expect(await f.Department.PostAsJsonAsync(route + "/handover-complete", f.LoanConditions(2)), HttpStatusCode.NotFound);
        await Expect(await f.Staff.PostAsJsonAsync(route + "/handover-complete", f.LoanConditions(2)), HttpStatusCode.NoContent);
        await Expect(await f.Staff.PostAsJsonAsync(route + "/return", f.LoanConditions(3)), HttpStatusCode.NoContent);
        Assert.Equal("PendingReturnConfirmation", (await Get(f.Staff, route)).GetProperty("status").GetString());
        await f.InDb(async db => Assert.Single(await db.DeviceOperationHolds.ToListAsync()));
        await Expect(await f.Staff.PostAsJsonAsync(route + "/confirm-return", f.LoanConditions(4)), HttpStatusCode.Forbidden);
        await Expect(await f.Department.PostAsJsonAsync(route + "/confirm-return", f.LoanConditions(4)), HttpStatusCode.NoContent);
        await f.InDb(async db => Assert.Empty(await db.DeviceOperationHolds.ToListAsync()));
        Assert.Equal("Returned", (await Get(f.Staff, route)).GetProperty("status").GetString());
    }

    [PostgreSqlFact]
    public async Task Allocation_requires_receiver_intent_and_only_receiver_can_complete()
    {
        await using var f = await Fixture.CreateAsync();
        await f.InDb(async db =>
        {
            var receiver = await db.Accounts.SingleAsync(x => x.AccountCode == "TK_RECEIVER");
            (await db.UserProfiles.SingleAsync(x => x.Id == receiver.UserProfileId)).DepartmentId = (await db.Departments.SingleAsync(x => x.Code == "PB_TARGET")).Id;
            await db.SaveChangesAsync();
        });
        var transfer = await f.CreateTransfer();
        Assert.Equal("Allocation", transfer.GetProperty("transferType").GetString());
        Assert.Equal("PendingConfirmation", transfer.GetProperty("status").GetString());
        var route = Transfers + "/" + transfer.GetProperty("transferCode").GetString();
        await Expect(await f.Manager.PostAsJsonAsync(route + "/approve", new { }), HttpStatusCode.Conflict);
        await Expect(await f.Staff.PostAsync(route + "/confirm-receipt-intent", null), HttpStatusCode.NotFound);
        await Expect(await f.Department.PostAsJsonAsync(route + "/handover-complete", f.Conditions()), HttpStatusCode.Conflict);
        await Expect(await f.Department.PostAsync(route + "/confirm-receipt-intent", null), HttpStatusCode.NoContent);
        var ready = await Get(f.Staff, route);
        Assert.Equal("Approved", ready.GetProperty("status").GetString());
        Assert.Equal("TK_DEPARTMENT", ready.GetProperty("receiverConfirmedAccountCode").GetString());
        Assert.Equal(JsonValueKind.Null, ready.GetProperty("decisionAccountCode").ValueKind);
        await f.InDb(async db =>
        {
            Assert.Equal((await db.Departments.SingleAsync(x => x.Code == "PB_TEST")).Id, (await db.Devices.SingleAsync(x => x.DeviceCode == "TB-ROLE-001")).DepartmentId);
            Assert.Single(await db.DeviceOperationHolds.ToListAsync());
        });
        await Expect(await f.Staff.PostAsJsonAsync(route + "/handover-complete", f.Conditions()), HttpStatusCode.NotFound);
        await Expect(await f.Department.PostAsJsonAsync(route + "/handover-complete", new { targetHandoverAccountCode = "TK_STAFF", items = new[] { new { deviceCode = "TB-ROLE-001", condition = "Good" } } }), HttpStatusCode.BadRequest);
        await Expect(await f.Receiver.PostAsJsonAsync(route + "/handover-complete", f.Conditions()), HttpStatusCode.NoContent);
        await Expect(await f.Department.PostAsJsonAsync(route + "/handover-complete", f.Conditions()), HttpStatusCode.Conflict);
        await f.InDb(async db =>
        {
            var completed = await db.DeviceTransfers.SingleAsync();
            Assert.Equal((await db.Accounts.SingleAsync(x => x.AccountCode == "TK_RECEIVER")).Id, completed.TargetHandoverAccountId);
            Assert.Empty(await db.DeviceOperationHolds.ToListAsync());
            Assert.Equal(completed.TargetDepartmentId, (await db.Devices.SingleAsync(x => x.DeviceCode == "TB-ROLE-001")).DepartmentId);
        });
    }

    [PostgreSqlFact]
    public async Task Interdepartment_requires_receiver_then_equipment_approval_and_receiver_completion()
    {
        await using var f = await Fixture.CreateAsync();
        await f.InDb(async db => { (await db.Devices.SingleAsync(x => x.DeviceCode == "TB-ROLE-001")).DepartmentId = (await db.Departments.SingleAsync(x => x.Code == "PB_TARGET")).Id; await db.SaveChangesAsync(); });
        var transfer = await f.CreateTransfer(false);
        Assert.Equal("InterDepartment", transfer.GetProperty("transferType").GetString());
        var route = Transfers + "/" + transfer.GetProperty("transferCode").GetString();
        await Expect(await f.Staff.PostAsJsonAsync(route + "/approve", new { }), HttpStatusCode.Conflict);
        await Expect(await f.Receiver.PostAsync(route + "/confirm-receipt-intent", null), HttpStatusCode.NoContent);
        Assert.Equal("PendingApproval", (await Get(f.Staff, route)).GetProperty("status").GetString());
        await f.InDb(async db => Assert.Empty(await db.DeviceOperationHolds.ToListAsync()));
        await Expect(await f.Receiver.PostAsJsonAsync(route + "/handover-complete", f.Conditions()), HttpStatusCode.Conflict);
        await Expect(await f.Department.PutAsJsonAsync(route, f.TransferBody(false)), HttpStatusCode.Conflict);
        await Expect(await f.Department.PostAsJsonAsync(route + "/approve", new { }), HttpStatusCode.Forbidden);
        await Expect(await f.Staff.PostAsJsonAsync(route + "/approve", new { }), HttpStatusCode.NoContent);
        await Expect(await f.Department.PostAsJsonAsync(route + "/handover-complete", f.Conditions()), HttpStatusCode.NotFound);
        await Expect(await f.Receiver.PostAsJsonAsync(route + "/handover-complete", f.Conditions()), HttpStatusCode.NoContent);
    }

    [PostgreSqlFact]
    public async Task Receiver_can_reject_without_creating_an_equipment_decision()
    {
        await using var f = await Fixture.CreateAsync();
        var transfer = await f.CreateTransfer();
        var route = Transfers + "/" + transfer.GetProperty("transferCode").GetString();
        await Expect(await f.Department.PostAsJsonAsync(route + "/reject-receipt-intent", new { reason = "No longer required" }), HttpStatusCode.NoContent);
        var rejected = await Get(f.Staff, route);
        Assert.Equal("Rejected", rejected.GetProperty("status").GetString());
        Assert.Equal("TK_DEPARTMENT", rejected.GetProperty("receiverRejectedAccountCode").GetString());
        Assert.Equal("No longer required", rejected.GetProperty("receiverRejectionReason").GetString());
        Assert.Equal(JsonValueKind.Null, rejected.GetProperty("decisionAccountCode").ValueKind);
        await f.InDb(async db => Assert.Empty(await db.DeviceOperationHolds.ToListAsync()));
    }

    [PostgreSqlFact]
    public async Task Source_only_creation_and_manager_participation_are_guarded()
    {
        await using var f = await Fixture.CreateAsync();
        await Expect(await f.Department.PostAsJsonAsync(Transfers, f.TransferBody()), HttpStatusCode.Forbidden);
        await Expect(await f.Staff.PostAsJsonAsync(Transfers, f.TransferBody(false)), HttpStatusCode.Forbidden);
        await Expect(await f.Manager.PostAsJsonAsync(Transfers, f.TransferBody()), HttpStatusCode.Forbidden);
        await Expect(await f.ForeignStaff.PostAsJsonAsync(Transfers, f.TransferBody()), HttpStatusCode.Forbidden);
        await Expect(await f.Department.PostAsJsonAsync(Transfers, new { sourceDepartmentCode = "PB_TARGET", targetDepartmentCode = "PB_TEST", targetResponsibleAccountCode = "TK_STAFF", reason = "Return", items = new[] { new { deviceCode = "TB-ROLE-001" } } }), HttpStatusCode.Conflict);
    }

    [PostgreSqlFact]
    public async Task Equipment_staff_can_lend_to_another_department_but_cannot_approve_own_loan()
    {
        await using var f = await Fixture.CreateAsync();
        var loan = await f.CreateLoan(f.Department, "TB-ROLE-002");
        var route = Loans + "/" + loan.GetProperty("loanCode").GetString();
        await Expect(await f.ForeignStaff.PostAsJsonAsync(route + "/approve", new { version = 1 }), HttpStatusCode.Forbidden);
        await Expect(await f.Department.PostAsJsonAsync(route + "/approve", new { version = 1 }), HttpStatusCode.Forbidden);
        await Expect(await f.Staff.PostAsJsonAsync(route + "/approve", new { version = 1 }), HttpStatusCode.NoContent);
        await Expect(await f.Department.PostAsJsonAsync(route + "/handover-complete", f.LoanConditions(2, "TB-ROLE-002")), HttpStatusCode.NoContent);
        await Expect(await f.Department.PostAsJsonAsync(route + "/return", f.LoanConditions(3, "TB-ROLE-002")), HttpStatusCode.NoContent);
        await Expect(await f.Staff.PostAsJsonAsync(route + "/confirm-return", f.LoanConditions(4, "TB-ROLE-002")), HttpStatusCode.NoContent);
    }

    [PostgreSqlFact]
    public async Task Competing_allocations_only_reserve_once_and_receiver_confirmation_rolls_back_on_failure()
    {
        await using var f = await Fixture.CreateAsync();
        var first = await f.CreateTransfer();
        var second = await f.CreateTransfer();
        var firstRoute = Transfers + "/" + first.GetProperty("transferCode").GetString();
        var secondRoute = Transfers + "/" + second.GetProperty("transferCode").GetString();
        var results = await Task.WhenAll(f.Department.PostAsync(firstRoute + "/confirm-receipt-intent", null), f.Department.PostAsync(secondRoute + "/confirm-receipt-intent", null));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.NoContent);
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        foreach (var result in results) result.Dispose();
        await f.InDb(async db =>
        {
            Assert.Single(await db.DeviceOperationHolds.ToListAsync());
            Assert.Single(await db.DeviceTransfers.Where(x => x.Status == CSVC.Core.DeviceTransfers.DeviceTransferStatus.PendingConfirmation && x.ReceiverConfirmedAccountId == null).ToListAsync());
            Assert.Single(await db.DeviceTransferHistories.Where(x => x.ActionCode == "TRANSFER_RECEIVER_CONFIRMED").ToListAsync());
        });
    }

    [PostgreSqlFact]
    public async Task Allocation_confirmation_audit_failure_rolls_back_reservations_and_can_be_retried()
    {
        await using var f = await Fixture.CreateAsync();
        var transfer = await f.CreateTransfer();
        var route = Transfers + "/" + transfer.GetProperty("transferCode").GetString();
        f.FailNextTransferAction("TRANSFER_RECEIVER_CONFIRMED");
        await Expect(await f.Department.PostAsync(route + "/confirm-receipt-intent", null), HttpStatusCode.ServiceUnavailable);
        await f.InDb(async db =>
        {
            Assert.Empty(await db.DeviceOperationHolds.ToListAsync());
            Assert.Null((await db.DeviceTransfers.SingleAsync()).ReceiverConfirmedAt);
        });
        await Expect(await f.Department.PostAsync(route + "/confirm-receipt-intent", null), HttpStatusCode.NoContent);
        f.FailNextTransferAction("TRANSFER_COMPLETED");
        await Expect(await f.Department.PostAsJsonAsync(route + "/handover-complete", f.Conditions()), HttpStatusCode.ServiceUnavailable);
        await f.InDb(async db =>
        {
            var item = await db.DeviceTransfers.SingleAsync();
            Assert.Equal(CSVC.Core.DeviceTransfers.DeviceTransferStatus.Approved, item.Status);
            Assert.Equal(item.SourceDepartmentId, (await db.Devices.SingleAsync(x => x.DeviceCode == "TB-ROLE-001")).DepartmentId);
            Assert.Single(await db.DeviceOperationHolds.ToListAsync());
        });
        await Expect(await f.Department.PostAsJsonAsync(route + "/handover-complete", f.Conditions()), HttpStatusCode.NoContent);
    }

    [PostgreSqlFact]
    public async Task Equipment_department_cannot_change_while_transfer_is_open_but_same_selection_is_allowed()
    {
        await using var f = await Fixture.CreateAsync();
        var transfer = await f.CreateTransfer();
        const string settingsRoute = "/api/v1/settings/equipment-department";
        await Expect(await f.Manager.PutAsJsonAsync(settingsRoute, new { departmentCode = "PB_TARGET", version = 1 }), HttpStatusCode.Conflict);
        await Expect(await f.Manager.PutAsJsonAsync(settingsRoute, new { departmentCode = "PB_TEST", version = 1 }), HttpStatusCode.NoContent);
        await Expect(await f.Staff.PostAsync(Transfers + "/" + transfer.GetProperty("transferCode").GetString() + "/cancel", null), HttpStatusCode.NoContent);
        await Expect(await f.Manager.PutAsJsonAsync(settingsRoute, new { departmentCode = "PB_TARGET", version = 1 }), HttpStatusCode.NoContent);
    }

    [PostgreSqlTheory]
    [InlineData(DeviceCondition.Good)]
    [InlineData(DeviceCondition.Damaged)]
    public async Task Recovery_accepts_good_and_damaged_and_records_actual_receiver_separately_from_responsible(DeviceCondition condition)
    {
        await using var f = await Fixture.CreateAsync();
        await f.PrepareRecovery(condition);
        await f.InDb(async db =>
        {
            var other = await db.Accounts.SingleAsync(x => x.AccountCode == "TK_FOREIGN");
            (await db.UserProfiles.SingleAsync(x => x.Id == other.UserProfileId)).DepartmentId = (await db.Departments.SingleAsync(x => x.Code == "PB_TEST")).Id;
            await db.SaveChangesAsync();
        });
        var transfer = await f.CreateRecovery();
        var route = Transfers + "/" + transfer.GetProperty("transferCode").GetString();
        Assert.Equal("Recovery", transfer.GetProperty("transferType").GetString());
        Assert.Equal(condition.ToString(), transfer.GetProperty("items")[0].GetProperty("currentCondition").GetString());
        await Expect(await f.Department.PostAsync(route + "/confirm-receipt-intent", null), HttpStatusCode.NotFound);
        await Expect(await f.Manager.PostAsync(route + "/confirm-receipt-intent", null), HttpStatusCode.Forbidden);
        await Expect(await f.Staff.PostAsJsonAsync(route + "/approve", new { }), HttpStatusCode.Conflict);
        await Expect(await f.Staff.PostAsJsonAsync(route + "/handover-complete", f.RecoveryConditions(condition)), HttpStatusCode.Conflict);
        await Expect(await f.Department.PutAsJsonAsync(route, f.RecoveryBody()), HttpStatusCode.NoContent);
        await Expect(await f.Staff.PostAsync(route + "/confirm-receipt-intent", null), HttpStatusCode.NoContent);
        var accepted = await Get(f.Staff, route);
        Assert.Equal("Approved", accepted.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, accepted.GetProperty("decisionAccountCode").ValueKind);
        await f.InDb(async db =>
        {
            var pending = await db.DeviceTransfers.SingleAsync();
            Assert.Equal(pending.SourceDepartmentId, (await db.Devices.SingleAsync(x => x.DeviceCode == "TB-ROLE-001")).DepartmentId);
            Assert.Single(await db.DeviceOperationHolds.ToListAsync());
        });
        await Expect(await f.Department.PutAsJsonAsync(route, f.RecoveryBody()), HttpStatusCode.Conflict);
        await Expect(await f.Department.PostAsync(route + "/cancel", null), HttpStatusCode.Conflict);
        await Expect(await f.Manager.PutAsJsonAsync("/api/v1/settings/equipment-department", new { departmentCode = "PB_TARGET", version = 1 }), HttpStatusCode.Conflict);
        await Expect(await f.Staff.PostAsJsonAsync(route + "/approve", new { }), HttpStatusCode.Conflict);
        await Expect(await f.Staff.PostAsJsonAsync(route + "/reject", new { }), HttpStatusCode.Conflict);
        await Expect(await f.Staff.PostAsJsonAsync(route + "/handover-complete", new { items = new[] { new { deviceCode = "TB-ROLE-001" } } }), HttpStatusCode.BadRequest);
        await Expect(await f.Staff.PostAsJsonAsync(route + "/handover-complete", f.RecoveryConditions(DeviceCondition.Missing)), HttpStatusCode.BadRequest);
        await Expect(await f.ForeignStaff.PostAsJsonAsync(route + "/handover-complete", f.RecoveryConditions(condition)), HttpStatusCode.NoContent);
        await Expect(await f.Staff.PostAsJsonAsync(route + "/handover-complete", f.RecoveryConditions(condition)), HttpStatusCode.Conflict);
        await f.InDb(async db =>
        {
            var done = await db.DeviceTransfers.SingleAsync();
            var device = await db.Devices.SingleAsync(x => x.DeviceCode == "TB-ROLE-001");
            Assert.Equal(done.TargetDepartmentId, device.DepartmentId);
            Assert.Equal(condition, device.Condition);
            Assert.Null(device.AreaId);
            Assert.Equal((await db.Accounts.SingleAsync(x => x.AccountCode == "TK_MANAGER")).Id, device.ResponsibleAccountId);
            Assert.Equal((await db.Accounts.SingleAsync(x => x.AccountCode == "TK_FOREIGN")).Id, done.TargetHandoverAccountId);
            Assert.Empty(await db.DeviceOperationHolds.ToListAsync());
            var history = await db.DeviceHistories.SingleAsync(x => x.ActionCode == "DEVICE_RECOVERY_COMPLETED_TB-ROLE-001");
            Assert.Equal("Recovery", JsonDocument.Parse(history.AfterData!).RootElement.GetProperty("transferType").GetString());
        });
    }

    [PostgreSqlFact]
    public async Task Recovery_receiver_requires_equipment_staff_role_even_inside_equipment_department()
    {
        await using var f = await Fixture.CreateAsync();
        await f.PrepareRecovery(DeviceCondition.Damaged);
        await f.InDb(async db =>
        {
            var receiver = await db.Accounts.SingleAsync(x => x.AccountCode == "TK_RECEIVER");
            (await db.UserProfiles.SingleAsync(x => x.Id == receiver.UserProfileId)).DepartmentId = (await db.Departments.SingleAsync(x => x.Code == "PB_TEST")).Id;
            await db.SaveChangesAsync();
        });
        var transfer = await f.CreateRecovery();
        var route = Transfers + "/" + transfer.GetProperty("transferCode").GetString();
        await Expect(await f.Receiver.PostAsync(route + "/confirm-receipt-intent", null), HttpStatusCode.Forbidden);
        await Expect(await f.Receiver.PostAsJsonAsync(route + "/reject-receipt-intent", new { }), HttpStatusCode.Forbidden);
        await Expect(await f.ForeignStaff.PostAsync(route + "/confirm-receipt-intent", null), HttpStatusCode.Forbidden);
        await Expect(await f.Staff.PostAsJsonAsync(route + "/reject-receipt-intent", new { reason = "Recheck receipt details" }), HttpStatusCode.NoContent);
        var rejected = await Get(f.Staff, route);
        Assert.Equal("Rejected", rejected.GetProperty("status").GetString());
        Assert.Equal("TK_STAFF", rejected.GetProperty("receiverRejectedAccountCode").GetString());
        Assert.Equal(JsonValueKind.Null, rejected.GetProperty("decisionAccountCode").ValueKind);
        await f.InDb(async db => Assert.Empty(await db.DeviceOperationHolds.ToListAsync()));
        transfer = await f.CreateRecovery();
        route = Transfers + "/" + transfer.GetProperty("transferCode").GetString();
        await Expect(await f.Staff.PostAsync(route + "/confirm-receipt-intent", null), HttpStatusCode.NoContent);
        await Expect(await f.Receiver.PostAsJsonAsync(route + "/handover-complete", f.RecoveryConditions(DeviceCondition.Damaged)), HttpStatusCode.Forbidden);
        await Expect(await f.Manager.PostAsJsonAsync(route + "/handover-complete", f.RecoveryConditions(DeviceCondition.Damaged)), HttpStatusCode.Forbidden);
    }

    [PostgreSqlTheory]
    [InlineData("quantity", "DEVICE_NOT_RECEIVED", false)]
    [InlineData("quantity", "DEVICE_NOT_RECEIVED", true)]
    [InlineData("missing", "DEVICE_NOT_AVAILABLE", false)]
    [InlineData("missing", "DEVICE_NOT_AVAILABLE", true)]
    [InlineData("locked", "DEVICE_LOCKED", false)]
    [InlineData("locked", "DEVICE_LOCKED", true)]
    [InlineData("busy", "DEVICE_BUSY", false)]
    [InlineData("busy", "DEVICE_BUSY", true)]
    public async Task Recovery_rejects_unreceived_missing_locked_and_busy_devices_including_children(string invalid, string code, bool child)
    {
        await using var f = await Fixture.CreateAsync();
        await f.PrepareRecovery(DeviceCondition.Good, child);
        await f.InDb(async db =>
        {
            var device = await db.Devices.SingleAsync(x => x.DeviceCode == (child ? "TB-ROLE-002" : "TB-ROLE-001"));
            if (invalid == "quantity") device.Quantity = 0;
            if (invalid == "missing") device.Condition = DeviceCondition.Missing;
            if (invalid == "locked") device.Status = CSVC.Core.Common.EntityStatus.Locked;
            if (invalid == "busy") db.Add(new DeviceOperationHold { DeviceCode = device.DeviceCode, DeviceId = device.Id, OperationKind = DeviceOperationKind.Loan, OperationId = Guid.NewGuid(), HeldAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        });
        using var response = await f.Department.PostAsJsonAsync(Transfers, f.RecoveryBody());
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(code, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        await f.InDb(async db => Assert.Empty(await db.DeviceTransfers.ToListAsync()));
    }

    [PostgreSqlFact]
    public async Task Recovery_edit_reclassifies_but_damaged_devices_remain_forbidden_for_other_workflows()
    {
        await using var f = await Fixture.CreateAsync();
        await f.PrepareRecovery(DeviceCondition.Good);
        var transfer = await f.CreateRecovery();
        var route = Transfers + "/" + transfer.GetProperty("transferCode").GetString();
        await Expect(await f.Department.PutAsJsonAsync(route, f.TransferBody(false)), HttpStatusCode.NoContent);
        Assert.Equal("InterDepartment", (await Get(f.Staff, route)).GetProperty("transferType").GetString());
        await Expect(await f.Department.PutAsJsonAsync(route, f.RecoveryBody()), HttpStatusCode.NoContent);
        Assert.Equal("Recovery", (await Get(f.Staff, route)).GetProperty("transferType").GetString());
        await f.InDb(async db => { (await db.Devices.SingleAsync(x => x.DeviceCode == "TB-ROLE-001")).Condition = DeviceCondition.Damaged; await db.SaveChangesAsync(); });
        await Expect(await f.Department.PutAsJsonAsync(route, f.TransferBody(false)), HttpStatusCode.Conflict);
        Assert.Equal("Recovery", (await Get(f.Staff, route)).GetProperty("transferType").GetString());
        await Expect(await f.Department.PostAsJsonAsync(Loans, new { lenderDepartmentCode = "PB_TARGET", purpose = "Invalid self loan", dueAt = DateTimeOffset.UtcNow.AddDays(7), items = new[] { new { deviceCode = "TB-ROLE-001" } } }), HttpStatusCode.Conflict);
        await Expect(await f.Department.PostAsync(route + "/cancel", null), HttpStatusCode.NoContent);
        await f.InDb(async db => { (await db.Devices.SingleAsync(x => x.DeviceCode == "TB-ROLE-001")).DepartmentId = (await db.Departments.SingleAsync(x => x.Code == "PB_TEST")).Id; await db.SaveChangesAsync(); });
        await Expect(await f.Staff.PostAsJsonAsync(Transfers, f.TransferBody()), HttpStatusCode.Conflict);
    }

    [PostgreSqlFact]
    public async Task Recovery_includes_damaged_children_and_audit_failures_rollback_the_whole_group()
    {
        await using var f = await Fixture.CreateAsync();
        await f.PrepareRecovery(DeviceCondition.Good, true);
        var transfer = await f.CreateRecovery();
        Assert.Equal(2, transfer.GetProperty("itemCount").GetInt32());
        var route = Transfers + "/" + transfer.GetProperty("transferCode").GetString();
        f.FailNextTransferAction("TRANSFER_RECEIVER_CONFIRMED");
        await Expect(await f.Staff.PostAsync(route + "/confirm-receipt-intent", null), HttpStatusCode.ServiceUnavailable);
        await f.InDb(async db => { Assert.Empty(await db.DeviceOperationHolds.ToListAsync()); Assert.Null((await db.DeviceTransfers.SingleAsync()).ReceiverConfirmedAt); });
        await Expect(await f.Staff.PostAsync(route + "/confirm-receipt-intent", null), HttpStatusCode.NoContent);
        var body = new { items = new[] { new { deviceCode = "TB-ROLE-001", condition = "Good" }, new { deviceCode = "TB-ROLE-002", condition = "Damaged" } } };
        await Expect(await f.Staff.PostAsJsonAsync(route + "/handover-complete", f.Conditions()), HttpStatusCode.BadRequest);
        f.FailNextTransferAction("TRANSFER_COMPLETED");
        await Expect(await f.Staff.PostAsJsonAsync(route + "/handover-complete", body), HttpStatusCode.ServiceUnavailable);
        await f.InDb(async db =>
        {
            var open = await db.DeviceTransfers.SingleAsync();
            Assert.Equal(2, await db.DeviceOperationHolds.CountAsync());
            Assert.All(await db.Devices.ToListAsync(), device => Assert.Equal(open.SourceDepartmentId, device.DepartmentId));
        });
        await Expect(await f.Staff.PostAsJsonAsync(route + "/handover-complete", body), HttpStatusCode.NoContent);
        await f.InDb(async db => { Assert.Empty(await db.DeviceOperationHolds.ToListAsync()); Assert.Equal(DeviceCondition.Damaged, (await db.Devices.SingleAsync(x => x.DeviceCode == "TB-ROLE-002")).Condition); });
    }

    [PostgreSqlFact]
    public async Task Recovery_revalidates_on_acceptance_and_completion_and_competing_requests_cannot_both_reserve()
    {
        await using var f = await Fixture.CreateAsync();
        await f.PrepareRecovery(DeviceCondition.Damaged);
        var first = await f.CreateRecovery();
        var second = await f.CreateRecovery();
        var firstRoute = Transfers + "/" + first.GetProperty("transferCode").GetString();
        var secondRoute = Transfers + "/" + second.GetProperty("transferCode").GetString();
        await f.InDb(async db => { (await db.Devices.SingleAsync(x => x.DeviceCode == "TB-ROLE-001")).Condition = DeviceCondition.Missing; await db.SaveChangesAsync(); });
        await Expect(await f.Staff.PostAsync(firstRoute + "/confirm-receipt-intent", null), HttpStatusCode.Conflict);
        await f.InDb(async db => { Assert.Empty(await db.DeviceOperationHolds.ToListAsync()); (await db.Devices.SingleAsync(x => x.DeviceCode == "TB-ROLE-001")).Condition = DeviceCondition.Damaged; await db.SaveChangesAsync(); });
        var responses = await Task.WhenAll(f.Staff.PostAsync(firstRoute + "/confirm-receipt-intent", null), f.Staff.PostAsync(secondRoute + "/confirm-receipt-intent", null));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.NoContent);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        var approvedRoute = responses[0].StatusCode == HttpStatusCode.NoContent ? firstRoute : secondRoute;
        foreach (var response in responses) response.Dispose();
        await f.InDb(async db => { (await db.Devices.SingleAsync(x => x.DeviceCode == "TB-ROLE-001")).Condition = DeviceCondition.Missing; await db.SaveChangesAsync(); });
        await Expect(await f.Staff.PostAsJsonAsync(approvedRoute + "/handover-complete", f.RecoveryConditions(DeviceCondition.Damaged)), HttpStatusCode.Conflict);
        await f.InDb(async db => Assert.Single(await db.DeviceOperationHolds.ToListAsync()));
    }

    private static async Task Expect(HttpResponseMessage response, HttpStatusCode expected)
    {
        using (response) Assert.True(response.StatusCode == expected, $"Expected {expected}, got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    private static async Task<JsonElement> Get(HttpClient client, string route)
    {
        using var response = await client.GetAsync(route);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    internal sealed class Fixture(AccountTestDatabase database, AccountApiTestHost host, HttpClient manager, HttpClient staff,
        HttpClient department, HttpClient foreignStaff, HttpClient receiver) : IAsyncDisposable
    {
        public HttpClient Receiver { get; } = receiver;
        public void FailNextTransferAction(string action) => host.Failures.FailNextTransferAction = action;
        public HttpClient Manager { get; } = manager;
        public HttpClient Staff { get; } = staff;
        public HttpClient Department { get; } = department;
        public HttpClient ForeignStaff { get; } = foreignStaff;
        public static async Task<Fixture> CreateAsync()
        {
            var database = await AccountTestDatabase.CreateAsync();
            var host = new AccountApiTestHost(database.ConnectionString);
            try
            {
                var seed = await host.SeedAsync();
                await using var scope = host.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
                var target = new Department { Code = "PB_TARGET", Name = "Target department" };
                var foreign = new Department { Code = "PB_FOREIGN", Name = "Unrelated department" };
                var profile = new UserProfile { FullName = "Department staff", DepartmentId = target.Id };
                var foreignProfile = new UserProfile { FullName = "Other equipment staff", DepartmentId = foreign.Id };
                var account = new Account { AccountCode = "TK_DEPARTMENT", Email = "department@example.com", RoleId = 3, UserProfileId = profile.Id, PasswordHash = "test-only" };
                var foreignAccount = new Account { AccountCode = "TK_FOREIGN", Email = "foreign@example.com", RoleId = 2, UserProfileId = foreignProfile.Id, PasswordHash = "test-only" };
                db.AddRange(target, foreign, profile, foreignProfile, account, foreignAccount,
                    AccountApiTestHost.Session(account.Id, new string('B', 64)), AccountApiTestHost.Session(foreignAccount.Id, new string('C', 64)));
                var receiverProfile = new UserProfile { FullName = "Receiver", DepartmentId = foreign.Id };
                var receiverAccount = new Account { AccountCode = "TK_RECEIVER", Email = "receiver@example.com", RoleId = 3, UserProfileId = receiverProfile.Id, PasswordHash = "test-only" };
                db.AddRange(receiverProfile, receiverAccount, AccountApiTestHost.Session(receiverAccount.Id, new string('F', 64)));
                var config = await db.EquipmentSettings.SingleAsync();
                config.EquipmentDepartmentId = seed.Department;
                config.Version = 1;
                var category = new DeviceCategory { DisplayCode = "CAT_ROLE", Name = "Role test" };
                db.Add(category);
                foreach (var number in Enumerable.Range(1, 3))
                {
                    var code = "TB-ROLE-00" + number;
                    var barcode = "BC" + new string((char)('D' + number), 30);
                    db.Add(new DeviceIdentity { DeviceCode = code, Barcode = barcode, ReservedAt = host.Clock.GetUtcNow() });
                    db.Add(new Device { DeviceCode = code, Barcode = barcode, Name = "Device " + number, CategoryId = category.Id,
                        DepartmentId = number == 3 ? target.Id : seed.Department, ResponsibleAccountId = number == 3 ? account.Id : seed.Staff, Quantity = 1 });
                }
                await db.SaveChangesAsync();
                HttpClient Client(string token)
                {
                    var client = host.CreateClient();
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    return client;
                }
                return new(database, host, Client(seed.ManagerToken), Client(seed.StaffToken), Client(new string('B', 64)), Client(new string('C', 64)), Client(new string('F', 64)));
            }
            catch { await host.DisposeAsync(); await database.DisposeAsync(); throw; }
        }
        public async Task PrepareRecovery(DeviceCondition condition, bool withChild = false)
            => await InDb(async db =>
            {
                var department = await db.Departments.SingleAsync(x => x.Code == "PB_TARGET");
                var device = await db.Devices.SingleAsync(x => x.DeviceCode == "TB-ROLE-001");
                device.DepartmentId = department.Id;
                device.Condition = condition;
                if (withChild)
                {
                    var child = await db.Devices.SingleAsync(x => x.DeviceCode == "TB-ROLE-002");
                    child.DepartmentId = department.Id;
                    child.ParentDeviceId = device.Id;
                    child.Condition = DeviceCondition.Damaged;
                }
                await db.SaveChangesAsync();
            });
        public object RecoveryBody() => new { sourceDepartmentCode = "PB_TARGET", targetDepartmentCode = "PB_TEST", targetResponsibleAccountCode = "TK_MANAGER", reason = "Recover devices to equipment department", items = new[] { new { deviceCode = "TB-ROLE-001" } } };
        public object RecoveryConditions(DeviceCondition condition) => new { items = new[] { new { deviceCode = "TB-ROLE-001", condition = condition.ToString() } } };
        public async Task<JsonElement> CreateRecovery()
        {
            using var response = await Department.PostAsJsonAsync(Transfers, RecoveryBody());
            Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        public object LoanBody(string deviceCode = "TB-ROLE-003") => new
        {
            version = 1, lenderDepartmentCode = deviceCode == "TB-ROLE-003" ? "PB_TARGET" : "PB_TEST", purpose = "Borrow from another department", dueAt = host.Clock.GetUtcNow().AddDays(7), items = new[] { new { deviceCode } }
        };
        public object LoanConditions(int version, string deviceCode = "TB-ROLE-003") => new { version, items = new[] { new { deviceCode, condition = "Good" } } };
        public object Conditions(string deviceCode = "TB-ROLE-001") => new { items = new[] { new { deviceCode, condition = "Good" } } };
        public object TransferBody(bool equipmentIsSource = true) => new
        {
            sourceDepartmentCode = equipmentIsSource ? "PB_TEST" : "PB_TARGET", targetDepartmentCode = equipmentIsSource ? "PB_TARGET" : "PB_FOREIGN",
            targetResponsibleAccountCode = equipmentIsSource ? "TK_DEPARTMENT" : "TK_RECEIVER", reason = "Transfer between departments",
            items = new[] { new { deviceCode = "TB-ROLE-001" } }
        };
        public async Task<JsonElement> CreateLoan(HttpClient actor, string code = "TB-ROLE-003")
        {
            using var response = await actor.PostAsJsonAsync(Loans, new { lenderDepartmentCode = code == "TB-ROLE-003" ? "PB_TARGET" : "PB_TEST", purpose = "Borrow", dueAt = host.Clock.GetUtcNow().AddDays(7), items = new[] { new { deviceCode = code } } });
            Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        public async Task<JsonElement> CreateTransfer(bool equipmentIsSource = true)
        {
            using var response = await (equipmentIsSource ? Staff : Department).PostAsJsonAsync(Transfers, TransferBody(equipmentIsSource));
            Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        public async Task InDb(Func<CsvcDbContext, Task> action)
        {
            await using var scope = host.Services.CreateAsyncScope();
            await action(scope.ServiceProvider.GetRequiredService<CsvcDbContext>());
        }
        public async ValueTask DisposeAsync()
        {
            Manager.Dispose(); Staff.Dispose(); Department.Dispose(); ForeignStaff.Dispose(); Receiver.Dispose();
            await host.DisposeAsync(); await database.DisposeAsync();
        }
    }
}
