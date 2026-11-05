using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CSVC.Core.DeviceTransfers;
using CSVC.Core.Devices;
using CSVC.Core.ServiceOrders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CSVC.API.Tests;

public sealed class EquipmentInitiatedRecoveryTests
{
    private const string Root = "/api/v1/device-transfers";
    private static object Body(Guid id, string device = "TB-ROLE-001", string reason = "Recover for redistribution")
        => new { requestId = id, sourceDepartmentCode = "PB_TARGET", targetResponsibleAccountCode = "TK_MANAGER", reason, items = new[] { new { deviceCode = device } } };
    private static object Conditions(int version, bool child = false, string rootCondition = "Good")
        => new { version, items = child ? new[] { new { deviceCode = "TB-ROLE-001", condition = rootCondition, note = "Recorded" }, new { deviceCode = "TB-ROLE-002", condition = "Damaged", note = "Recorded" } }
            : new[] { new { deviceCode = "TB-ROLE-001", condition = rootCondition, note = "Recorded" } } };

    [PostgreSqlTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task One_order_covers_creation_source_acceptance_dispatch_and_equipment_receipt(bool managerCreates)
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync();
        await f.PrepareRecovery(DeviceCondition.Good, true);
        var id = Guid.NewGuid(); var creator = managerCreates ? f.Manager : f.Staff;
        var created = await Create(creator, Body(id)); var code = Code(created);
        Assert.Equal("EquipmentDepartment", created.GetProperty("initiationType").GetString());
        Assert.Equal("AwaitingSourceAcceptance", created.GetProperty("workflowStage").GetString());
        Assert.Equal(2, created.GetProperty("items").GetArrayLength());
        Assert.Equal(code, Code(await Create(creator, Body(id))));
        await f.InDb(async db => { Assert.Single(await db.DeviceTransfers.ToListAsync()); Assert.Empty(await db.DeviceOperationHolds.ToListAsync()); });
        await Expect(await creator.PostAsJsonAsync(Root + "/recovery-orders", Body(id, reason: "Different")), HttpStatusCode.Conflict);
        var sourceView = await Get(f.Department, code);
        Assert.Equal(2, sourceView.GetProperty("items").GetArrayLength());
        await Post(f.Department, code, "confirm-recovery-intent", new { version = 1 });
        await f.InDb(async db => { Assert.Equal(2, await db.DeviceOperationHolds.CountAsync()); Assert.Equal("PB_TARGET", (await db.Departments.SingleAsync(d => d.Id == (db.Devices.Single(d => d.DeviceCode == "TB-ROLE-001").DepartmentId))).Code); });
        using var earlyReceipt = await f.Staff.PostAsJsonAsync(Root + "/" + code + "/handover-complete", Conditions(2, true));
        Assert.Equal(HttpStatusCode.Conflict, earlyReceipt.StatusCode);
        Assert.Contains("RECOVERY_SOURCE_HANDOVER_REQUIRED", await earlyReceipt.Content.ReadAsStringAsync());
        await Post(f.Department, code, "confirm-source-handover", Conditions(2, true, "Damaged"));
        var dispatched = await Get(f.Staff, code);
        Assert.Equal("AwaitingEquipmentReceipt", dispatched.GetProperty("workflowStage").GetString());
        await f.InDb(async db => { Assert.Equal(DeviceCondition.Good, (await db.Devices.SingleAsync(d => d.DeviceCode == "TB-ROLE-001")).Condition); Assert.Equal(2, await db.DeviceOperationHolds.CountAsync()); });
        await Post(f.Staff, code, "handover-complete", Conditions(3, true));
        var complete = await Get(f.Department, code);
        Assert.Equal("Completed", complete.GetProperty("status").GetString());
        Assert.Equal(2, complete.GetProperty("items").GetArrayLength()); // Remains visible after ownership changes.
        Assert.Equal("TK_DEPARTMENT", complete.GetProperty("sourceAcceptedByAccountCode").GetString());
        Assert.Equal("TK_DEPARTMENT", complete.GetProperty("sourceHandoverConfirmedByAccountCode").GetString());
        Assert.Equal("TK_STAFF", complete.GetProperty("targetHandoverAccountCode").GetString());
        Assert.Equal(JsonValueKind.Null, complete.GetProperty("receiverConfirmedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, complete.GetProperty("decisionAccountCode").ValueKind);
        var root = complete.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("deviceCode").GetString() == "TB-ROLE-001");
        Assert.Equal("Damaged", root.GetProperty("conditionAtSourceHandover").GetString());
        Assert.Equal("Good", root.GetProperty("conditionAtHandover").GetString());
        await f.InDb(async db =>
        {
            Assert.Single(await db.DeviceTransfers.ToListAsync()); Assert.Empty(await db.DeviceOperationHolds.ToListAsync());
            Assert.Empty(await db.DeviceReceipts.ToListAsync()); Assert.Empty(await db.DeviceLoans.ToListAsync());
            var manager = await db.Accounts.SingleAsync(a => a.AccountCode == "TK_MANAGER");
            foreach (var device in await db.Devices.Where(d => d.DeviceCode == "TB-ROLE-001" || d.DeviceCode == "TB-ROLE-002").ToListAsync())
            { Assert.Equal(manager.Id, device.ResponsibleAccountId); Assert.Null(device.AreaId); Assert.Equal("PB_TEST", (await db.Departments.SingleAsync(d => d.Id == device.DepartmentId)).Code); }
            Assert.Single(await db.DeviceTransferHistories.Where(h => h.ActionCode == "RECOVERY_ORDER_CREATED").ToListAsync());
            Assert.Single(await db.DeviceTransferHistories.Where(h => h.ActionCode == "RECOVERY_SOURCE_HANDED_OVER").ToListAsync());
        });
    }

    [PostgreSqlFact]
    public async Task Source_rejects_same_order_and_creator_can_edit_or_cancel_before_acceptance()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync(); await f.PrepareRecovery(DeviceCondition.Good);
        var code = Code(await Create(f.Manager, Body(Guid.NewGuid())));
        await Expect(await f.Staff.PutAsJsonAsync(Root + "/recovery-orders/" + code, EditBody(1)), HttpStatusCode.Forbidden);
        await Expect(await f.Manager.PutAsJsonAsync(Root + "/recovery-orders/" + code, EditBody(1)), HttpStatusCode.NoContent);
        await Expect(await f.Department.PostAsJsonAsync(Root + "/" + code + "/confirm-recovery-intent", new { version = 1 }), HttpStatusCode.Conflict);
        await Expect(await f.Department.PostAsJsonAsync(Root + "/" + code + "/reject-recovery-intent", new { version = 2 }), HttpStatusCode.BadRequest);
        await Post(f.Department, code, "reject-recovery-intent", new { version = 2, reason = "Still needed by the department" });
        var rejected = await Get(f.Manager, code);
        Assert.Equal("Rejected", rejected.GetProperty("status").GetString());
        Assert.Equal("Still needed by the department", rejected.GetProperty("sourceRejectionReason").GetString());
        Assert.Equal(1, rejected.GetProperty("items").GetArrayLength());
        var cancel = Code(await Create(f.Manager, Body(Guid.NewGuid())));
        await Expect(await f.Department.PostAsJsonAsync(Root + "/" + cancel + "/cancel", new { version = 1, reason = "Cancel" }), HttpStatusCode.Forbidden);
        await Post(f.Manager, cancel, "cancel", new { version = 1, reason = "No longer required" });
        Assert.Equal("Cancelled", (await Get(f.Department, cancel)).GetProperty("status").GetString());
        await f.InDb(async db => Assert.Empty(await db.DeviceOperationHolds.ToListAsync()));
    }

    [PostgreSqlFact]
    public async Task Wrong_actor_spoofed_payload_and_old_routes_cannot_bypass_the_new_workflow()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync(); await f.PrepareRecovery(DeviceCondition.Good);
        await Expect(await f.Department.PostAsJsonAsync(Root + "/recovery-orders", Body(Guid.NewGuid())), HttpStatusCode.Forbidden);
        await Expect(await f.ForeignStaff.PostAsJsonAsync(Root + "/recovery-orders", Body(Guid.NewGuid())), HttpStatusCode.Forbidden);
        await Expect(await f.Manager.PostAsJsonAsync(Root + "/recovery-orders", new { requestId = Guid.NewGuid(), sourceDepartmentCode = "PB_TARGET", targetDepartmentCode = "PB_FOREIGN", targetResponsibleAccountCode = "TK_MANAGER", reason = "Spoof", items = new[] { new { deviceCode = "TB-ROLE-001" } } }), HttpStatusCode.BadRequest);
        var code = Code(await Create(f.Staff, Body(Guid.NewGuid())));
        await Expect(await f.Staff.PostAsJsonAsync(Root + "/" + code + "/confirm-recovery-intent", new { version = 1 }), HttpStatusCode.Forbidden);
        await Expect(await f.Receiver.PostAsJsonAsync(Root + "/" + code + "/confirm-recovery-intent", new { version = 1 }), HttpStatusCode.NotFound);
        await Expect(await f.Staff.PostAsync(Root + "/" + code + "/confirm-receipt-intent", null), HttpStatusCode.Conflict);
        await Expect(await f.Manager.PostAsJsonAsync(Root + "/" + code + "/approve", new { }), HttpStatusCode.Conflict);
        await Expect(await f.Staff.PutAsJsonAsync(Root + "/" + code, f.RecoveryBody()), HttpStatusCode.Conflict);
        await Expect(await f.Department.PostAsJsonAsync(Root + "/" + code + "/confirm-recovery-intent", new { }), HttpStatusCode.BadRequest);
        await Post(f.Department, code, "confirm-recovery-intent", new { version = 1 });
        await Expect(await f.Manager.PostAsJsonAsync(Root + "/" + code + "/handover-complete", Conditions(2)), HttpStatusCode.Forbidden);
        await Expect(await f.Staff.PostAsJsonAsync(Root + "/" + code + "/confirm-source-handover", Conditions(2)), HttpStatusCode.Forbidden);
        await Expect(await f.Staff.PostAsJsonAsync(Root + "/" + code + "/cancel", new { version = 2, reason = "Cancel" }), HttpStatusCode.Conflict);
        await Expect(await f.Department.PostAsJsonAsync(Root + "/" + code + "/confirm-source-handover", new { version = 2, sourceHandoverAccountCode = "TK_DEPARTMENT", items = new[] { new { deviceCode = "TB-ROLE-001", condition = "Good" } } }), HttpStatusCode.BadRequest);
        await Post(f.Department, code, "confirm-source-handover", Conditions(2));
        await Expect(await f.Staff.PostAsJsonAsync(Root + "/" + code + "/handover-complete", new { version = 3, sourceHandoverAccountCode = "TK_DEPARTMENT", items = new[] { new { deviceCode = "TB-ROLE-001", condition = "Good" } } }), HttpStatusCode.BadRequest);
        await Expect(await f.Department.PostAsJsonAsync(Root + "/" + code + "/handover-complete", Conditions(3)), HttpStatusCode.NotFound);
    }

    [PostgreSqlFact]
    public async Task Competing_orders_only_one_source_acceptance_reserves()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync(); await f.PrepareRecovery(DeviceCondition.Good);
        var first = Code(await Create(f.Manager, Body(Guid.NewGuid()))); var second = Code(await Create(f.Manager, Body(Guid.NewGuid())));
        var results = await Task.WhenAll(f.Department.PostAsJsonAsync(Root + "/" + first + "/confirm-recovery-intent", new { version = 1 }), f.Department.PostAsJsonAsync(Root + "/" + second + "/confirm-recovery-intent", new { version = 1 }));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.NoContent); Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict); foreach (var r in results) r.Dispose();
        await f.InDb(async db => { Assert.Single(await db.DeviceOperationHolds.ToListAsync()); Assert.Single(await db.DeviceTransfers.Where(t => t.Status == DeviceTransferStatus.Approved).ToListAsync()); });
    }

    [PostgreSqlFact]
    public async Task Concurrent_create_retries_and_accept_edit_cancel_are_serialized()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync(); await f.PrepareRecovery(DeviceCondition.Good);
        var body = Body(Guid.NewGuid());
        var created = await Task.WhenAll(Create(f.Manager, body), Create(f.Manager, body));
        var code = Code(created[0]); Assert.Equal(code, Code(created[1]));
        var results = await Task.WhenAll(
            f.Department.PostAsJsonAsync(Root + "/" + code + "/confirm-recovery-intent", new { version = 1 }),
            f.Manager.PutAsJsonAsync(Root + "/recovery-orders/" + code, EditBody(1)),
            f.Manager.PostAsJsonAsync(Root + "/" + code + "/cancel", new { version = 1, reason = "Cancel" }));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.NoContent);
        Assert.Equal(2, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        foreach (var result in results) result.Dispose();
        await f.InDb(async db =>
        {
            var transfer = await db.DeviceTransfers.SingleAsync(); Assert.Equal(2, transfer.Version);
            Assert.Equal(transfer.Status == DeviceTransferStatus.Approved ? 1 : 0, await db.DeviceOperationHolds.CountAsync());
            Assert.Equal(2, await db.DeviceTransferHistories.CountAsync());
        });
    }

    [PostgreSqlTheory]
    [InlineData("RECOVERY_ORDER_CREATED")]
    [InlineData("RECOVERY_ORDER_UPDATED")]
    [InlineData("RECOVERY_SOURCE_ACCEPTED")]
    [InlineData("RECOVERY_SOURCE_REJECTED")]
    [InlineData("RECOVERY_SOURCE_HANDED_OVER")]
    [InlineData("TRANSFER_COMPLETED")]
    [InlineData("TRANSFER_CANCELLED")]
    public async Task Each_audit_failure_rolls_back_the_whole_transition(string action)
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync(); await f.PrepareRecovery(DeviceCondition.Good);
        if (action == "RECOVERY_ORDER_CREATED")
        {
            f.FailNextTransferAction(action);
            await Expect(await f.Manager.PostAsJsonAsync(Root + "/recovery-orders", Body(Guid.NewGuid())), HttpStatusCode.ServiceUnavailable);
            await f.InDb(async db => { Assert.Empty(await db.DeviceTransfers.ToListAsync()); Assert.Empty(await db.DeviceTransferItems.ToListAsync()); }); return;
        }
        var code = Code(await Create(f.Manager, Body(Guid.NewGuid())));
        if (action is "RECOVERY_SOURCE_HANDED_OVER" or "TRANSFER_COMPLETED") await Post(f.Department, code, "confirm-recovery-intent", new { version = 1 });
        if (action == "TRANSFER_COMPLETED") await Post(f.Department, code, "confirm-source-handover", Conditions(2));
        var before = await Get(f.Manager, code); f.FailNextTransferAction(action);
        var response = action switch
        {
            "RECOVERY_ORDER_UPDATED" => await f.Manager.PutAsJsonAsync(Root + "/recovery-orders/" + code, EditBody(1)),
            "RECOVERY_SOURCE_ACCEPTED" => await f.Department.PostAsJsonAsync(Root + "/" + code + "/confirm-recovery-intent", new { version = 1 }),
            "RECOVERY_SOURCE_REJECTED" => await f.Department.PostAsJsonAsync(Root + "/" + code + "/reject-recovery-intent", new { version = 1, reason = "Needed" }),
            "RECOVERY_SOURCE_HANDED_OVER" => await f.Department.PostAsJsonAsync(Root + "/" + code + "/confirm-source-handover", Conditions(2)),
            "TRANSFER_COMPLETED" => await f.Staff.PostAsJsonAsync(Root + "/" + code + "/handover-complete", Conditions(3)),
            _ => await f.Manager.PostAsJsonAsync(Root + "/" + code + "/cancel", new { version = 1, reason = "Cancel" })
        };
        await Expect(response, HttpStatusCode.ServiceUnavailable);
        var after = await Get(f.Manager, code);
        Assert.Equal(before.GetProperty("version").GetInt32(), after.GetProperty("version").GetInt32());
        Assert.Equal(before.GetProperty("workflowStage").GetString(), after.GetProperty("workflowStage").GetString());
        await f.InDb(async db => { Assert.Equal(action is "RECOVERY_SOURCE_HANDED_OVER" or "TRANSFER_COMPLETED" ? 1 : 0, await db.DeviceOperationHolds.CountAsync()); Assert.Equal("PB_TARGET", (await db.Departments.SingleAsync(d => d.Id == db.Devices.Single(x => x.DeviceCode == "TB-ROLE-001").DepartmentId)).Code); });
    }

    [PostgreSqlFact]
    public async Task Recovery_can_keep_service_holds_and_bad_or_missing_handover_items_cannot_complete()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync(); await f.PrepareRecovery(DeviceCondition.Good);
        using var response = await f.Staff.PostAsJsonAsync("/api/v1/service-orders", new { requestId = Guid.NewGuid(), orderType = "Maintenance", items = new[] { new { deviceCode = "TB-ROLE-001", executionMethod = "OnSite", workDescription = "Maintain" } } });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var source = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("displayCode").GetString();
        await Expect(await f.Manager.PostAsJsonAsync("/api/v1/service-orders/" + source + "/approve", new { requestId = Guid.NewGuid() }), HttpStatusCode.NoContent);
        var code = Code(await Create(f.Manager, Body(Guid.NewGuid())));
        await Post(f.Department, code, "confirm-recovery-intent", new { version = 1 });
        await Expect(await f.Department.PostAsJsonAsync(Root + "/" + code + "/confirm-source-handover", new { version = 2, items = Array.Empty<object>() }), HttpStatusCode.BadRequest);
        await Expect(await f.Department.PostAsJsonAsync(Root + "/" + code + "/confirm-source-handover", Conditions(2, rootCondition: "Missing")), HttpStatusCode.BadRequest);
        await Post(f.Department, code, "confirm-source-handover", Conditions(2));
        await Post(f.Staff, code, "handover-complete", Conditions(3));
        await f.InDb(async db => { Assert.Empty(await db.DeviceOperationHolds.ToListAsync()); Assert.Single(await db.Set<ServiceOrderDeviceHold>().ToListAsync()); });
    }

    [PostgreSqlFact]
    public async Task Migration_keeps_existing_source_initiated_recoveries_on_their_original_workflow()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync(); await f.PrepareRecovery(DeviceCondition.Good);
        var old = Code(await f.CreateRecovery());
        await f.InDb(async db =>
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261104082423_LoanTwoDepartmentWorkflow");
            await migrator.MigrateAsync();
            var transfer = await db.DeviceTransfers.SingleAsync();
            Assert.Equal(TransferInitiationType.SourceDepartment, transfer.InitiationType);
            Assert.Null(transfer.SourceAcceptedAt); Assert.Null(transfer.SourceHandoverConfirmedAt);
        });
        await Post(f.Staff, old, "confirm-receipt-intent", new { });
        await Post(f.Staff, old, "handover-complete", f.RecoveryConditions(DeviceCondition.Good));
    }

    private static object EditBody(int version) => new { version, sourceDepartmentCode = "PB_TARGET", targetResponsibleAccountCode = "TK_MANAGER", reason = "Updated", items = new[] { new { deviceCode = "TB-ROLE-001" } } };
    private static string Code(JsonElement transfer) => transfer.GetProperty("transferCode").GetString()!;
    private static async Task<JsonElement> Create(HttpClient client, object body)
    { using var response = await client.PostAsJsonAsync(Root + "/recovery-orders", body); Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync()); return await response.Content.ReadFromJsonAsync<JsonElement>(); }
    private static async Task<JsonElement> Get(HttpClient client, string code)
    { using var response = await client.GetAsync(Root + "/" + code); Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync()); return await response.Content.ReadFromJsonAsync<JsonElement>(); }
    private static async Task Post(HttpClient client, string code, string action, object body) => await Expect(await client.PostAsJsonAsync(Root + "/" + code + "/" + action, body), HttpStatusCode.NoContent);
    private static async Task Expect(HttpResponseMessage response, HttpStatusCode expected)
    { using (response) Assert.True(response.StatusCode == expected, $"Expected {expected}, got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}"); }
}
