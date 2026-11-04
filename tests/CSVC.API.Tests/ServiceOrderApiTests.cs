using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CSVC.Core.Accounts;
using CSVC.Core.Common;
using CSVC.Core.DeviceCategories;
using CSVC.Core.Devices;
using CSVC.Core.ServiceOrders;
using CSVC.Infrastructure.Persistence;
using CSVC.UseCases.ServiceOrders.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CSVC.API.Tests;

public sealed class ServiceOrderApiTests
{
    private const string Root = "/api/v1/service-orders";
    private const string Exports = "/api/v1/export-slips";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };

    [PostgreSqlFact]
    public async Task Create_only_source_is_idempotent_and_rejects_nested_legacy_exports()
    {
        await using var f = await Fixture.CreateAsync(); var request = f.Request();
        var a = await f.CreateOrder(request); var b = await f.CreateOrder(request);
        Assert.Equal(a.DisplayCode, b.DisplayCode); Assert.Empty(a.ExportSlips);
        await f.InDb(async db => { Assert.Empty(await db.ExportSlips.ToListAsync()); Assert.Empty(await db.Set<ServiceOrderDeviceHold>().ToListAsync()); });
        await Expect(await f.Staff.PostAsJsonAsync(Root, new { requestId = Guid.NewGuid(), items = request.Items, exportSlips = new object[0] }, Json), HttpStatusCode.BadRequest);
        await Expect(await f.Staff.PostAsJsonAsync(Root, new CreateServiceOrderRequest { RequestId = request.RequestId, Items = request.Items, Notes = "Changed" }, Json), HttpStatusCode.Conflict, "IDEMPOTENCY_CONFLICT");
        await Expect(await f.Staff.PostAsJsonAsync(Exports, f.ExportRequest(a.DisplayCode), Json), HttpStatusCode.Conflict);
    }

    [PostgreSqlFact]
    public async Task Approval_acquires_owned_holds_with_actor_time_and_retries_once()
    {
        await using var f = await Fixture.CreateAsync(); var order = await f.CreateOrder();
        var request = new ServiceTransitionRequest { RequestId = Guid.NewGuid(), Version = order.Version };
        await Expect(await f.Staff.PostAsJsonAsync(Root + "/" + order.DisplayCode + "/approve", request, Json), HttpStatusCode.Forbidden);
        await f.Approve(order.DisplayCode, request); await f.Approve(order.DisplayCode, request);
        await f.InDb(async db =>
        {
            var source = await db.ServiceOrders.SingleAsync(); var holds = await db.Set<ServiceOrderDeviceHold>().ToListAsync();
            Assert.Equal(3, holds.Count); Assert.All(holds, h => { Assert.Equal(source.ApprovedByAccountId, h.HeldByAccountId); Assert.Equal(source.ApprovedAt, h.HeldAt); Assert.Equal(source.Id, h.ServiceOrderId); });
            Assert.Empty(await db.DeviceOperationHolds.ToListAsync()); Assert.Equal(3, await db.ServiceOrderHistories.CountAsync(h => h.ActionCode == "HOLD_ACQUIRED"));
            Assert.Empty(await db.ExportSlips.ToListAsync());
        });
    }

    [PostgreSqlFact]
    public async Task Competing_source_approvals_have_one_winner_and_no_partial_hold_set()
    {
        await using var f = await Fixture.CreateAsync(); var a = await f.CreateOrder(); var b = await f.CreateOrder();
        var responses = await Task.WhenAll(new[] { a, b }.Select(o => f.Manager.PostAsJsonAsync(Root + "/" + o.DisplayCode + "/approve", new ServiceTransitionRequest { RequestId = Guid.NewGuid(), Version = 0 }, Json)));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.NoContent); Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        await f.InDb(async db => { Assert.Equal(3, await db.Set<ServiceOrderDeviceHold>().CountAsync()); Assert.Equal(3, await db.ServiceOrderItems.CountAsync(i => i.ExecutionStatus == null)); });
        foreach (var response in responses) response.Dispose();
    }

    [PostgreSqlFact]
    public async Task Existing_operation_hold_blocks_service_and_rolls_back_all_items()
    {
        await using var f = await Fixture.CreateAsync(); var order = await f.CreateOrder();
        await f.InDb(async db => { var d = await db.Devices.SingleAsync(d => d.DeviceCode == "TB_SERVICE_3"); db.DeviceOperationHolds.Add(new() { DeviceId = d.Id, DeviceCode = d.DeviceCode, OperationKind = DeviceOperationKind.ServiceOrder, OperationId = Guid.NewGuid(), HeldAt = f.Host.Clock.GetUtcNow() }); await db.SaveChangesAsync(); });
        await Expect(await f.Manager.PostAsJsonAsync(Root + "/" + order.DisplayCode + "/approve", new ServiceTransitionRequest { RequestId = Guid.NewGuid() }, Json), HttpStatusCode.Conflict, "DEVICE_BUSY");
        await f.InDb(async db => { Assert.Empty(await db.Set<ServiceOrderDeviceHold>().ToListAsync()); Assert.All(await db.ServiceOrderItems.ToListAsync(), i => Assert.Null(i.ExecutionStatus)); });
    }

    [PostgreSqlFact]
    public async Task Unchanged_legacy_writer_does_not_check_new_hold_characterizes_out_of_scope_gap()
    {
        await using var f = await Fixture.CreateAsync(); var order = await f.CreateOrder(); await f.Approve(order.DisplayCode);
        await f.InDb(async db => { var d = await db.Devices.SingleAsync(d => d.DeviceCode == "TB_SERVICE_2"); db.DeviceOperationHolds.Add(new() { DeviceId = d.Id, DeviceCode = d.DeviceCode, OperationKind = DeviceOperationKind.ServiceOrder, OperationId = Guid.NewGuid(), HeldAt = f.Host.Clock.GetUtcNow() }); await db.SaveChangesAsync(); Assert.True(await db.Set<ServiceOrderDeviceHold>().AnyAsync(h => h.DeviceId == d.Id)); });
        await Expect(await f.Staff.PostAsJsonAsync(Exports, f.ExportRequest(order.DisplayCode), Json), HttpStatusCode.Conflict, "DEVICE_BUSY");
    }

    [PostgreSqlTheory]
    [InlineData("SERVICE_CREATED")]
    [InlineData("SERVICE_APPROVED")]
    [InlineData("HOLD_ACQUIRED")]
    public async Task Source_and_hold_audit_failure_rolls_back_atomically(string action)
    {
        await using var f = await Fixture.CreateAsync(); ServiceOrderDto? order = null;
        if (action != "SERVICE_CREATED") order = await f.CreateOrder(); f.Host.Failures.FailNextServiceAction = action;
        var response = action == "SERVICE_CREATED" ? await f.Staff.PostAsJsonAsync(Root, f.Request(), Json) : await f.Manager.PostAsJsonAsync(Root + "/" + order!.DisplayCode + "/approve", new ServiceTransitionRequest { RequestId = Guid.NewGuid() }, Json);
        await Expect(response, HttpStatusCode.ServiceUnavailable);
        await f.InDb(async db => { Assert.Empty(await db.Set<ServiceOrderDeviceHold>().ToListAsync()); Assert.All(await db.ServiceOrderItems.ToListAsync(), i => Assert.Null(i.ExecutionStatus)); });
    }

    [PostgreSqlFact]
    public async Task Source_cancel_is_creator_only_and_does_not_create_child_documents()
    {
        await using var f = await Fixture.CreateAsync(); var order = await f.CreateOrder(); var r = new CancelServiceOrderRequest { RequestId = Guid.NewGuid(), Reason = "Not needed" };
        await Expect(await f.Manager.PostAsJsonAsync(Root + "/" + order.DisplayCode + "/cancel", r, Json), HttpStatusCode.Forbidden);
        await Expect(await f.Staff.PostAsJsonAsync(Root + "/" + order.DisplayCode + "/cancel", r, Json), HttpStatusCode.NoContent);
        await Expect(await f.Staff.PostAsJsonAsync(Root + "/" + order.DisplayCode + "/cancel", r, Json), HttpStatusCode.NoContent);
        Assert.Equal(ServiceWorkflowStatus.Canceled, (await f.GetOrder(order.DisplayCode)).Status);
    }

    [PostgreSqlFact]
    public async Task Export_selection_requires_external_matching_hold_and_current_equipment_department()
    {
        await using var f = await Fixture.CreateAsync(); var o = await f.CreateOrder(); await f.Approve(o.DisplayCode);
        await Expect(await f.Staff.PostAsJsonAsync(Exports, f.ExportRequest(o.DisplayCode, [1]), Json), HttpStatusCode.Conflict);
        await Expect(await f.Staff.PostAsJsonAsync(Exports, f.ExportRequest(o.DisplayCode, [2, 2]), Json), HttpStatusCode.BadRequest);
        await f.InDb(async db => { var other = new CSVC.Core.Departments.Department { Code = "PB_OTHER_V9", Name = "Other" }; db.Add(other); var d = await db.Devices.SingleAsync(d => d.DeviceCode == "TB_SERVICE_2"); d.DepartmentId = other.Id; await db.SaveChangesAsync(); });
        await Expect(await f.Staff.PostAsJsonAsync(Exports, f.ExportRequest(o.DisplayCode, [2]), Json), HttpStatusCode.Conflict, "DEVICE_NOT_EXPORTABLE");
    }

    [PostgreSqlFact]
    public async Task Export_edit_diffs_guards_preserves_survivors_and_retries_historical_keys()
    {
        await using var f = await Fixture.CreateAsync(); var o = await f.CreateOrder(); await f.Approve(o.DisplayCode); var e = await f.CreateExport(o.DisplayCode, [2]);
        Guid retained = Guid.Empty; await f.InDb(async db => retained = (await db.Set<ExportDeviceGuard>().SingleAsync()).ExportSlipItemId);
        var edit = new EditExportSlipRequest { RequestId = Guid.NewGuid(), Version = e.Version, FacilityName = "Workshop B", ItemNos = [2, 3], Notes = "Changed" };
        await Expect(await f.Staff.PutAsJsonAsync(Exports + "/" + e.DisplayCode, edit, Json), HttpStatusCode.NoContent);
        e = await f.GetExport(e.DisplayCode);
        var next = new EditExportSlipRequest { RequestId = Guid.NewGuid(), Version = e.Version, FacilityName = "Workshop C", ItemNos = [3] };
        await Expect(await f.Manager.PutAsJsonAsync(Exports + "/" + e.DisplayCode, next, Json), HttpStatusCode.NoContent);
        await Expect(await f.Staff.PutAsJsonAsync(Exports + "/" + e.DisplayCode, edit, Json), HttpStatusCode.NoContent);
        Assert.Equal("Workshop C", (await f.GetExport(e.DisplayCode)).FacilityName);
        await f.InDb(async db => { Assert.Single(await db.Set<ExportDeviceGuard>().ToListAsync()); Assert.False(await db.Set<ExportDeviceGuard>().AnyAsync(g => g.ExportSlipItemId == retained)); Assert.Equal(2, await db.ExportHistories.CountAsync(h => h.ActionCode == "EDIT")); });
        await Expect(await f.Staff.PutAsJsonAsync(Exports + "/" + e.DisplayCode, new { requestId = Guid.NewGuid(), version = e.Version, facilityName = "Other", itemNos = new[] { 3 }, serviceOrderCode = "SC_OTHER" }), HttpStatusCode.BadRequest);
    }

    [PostgreSqlFact]
    public async Task Competing_exports_and_stale_edits_cannot_share_a_device()
    {
        await using var f = await Fixture.CreateAsync(); var o = await f.CreateOrder(); await f.Approve(o.DisplayCode);
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => f.Staff.PostAsJsonAsync(Exports, f.ExportRequest(o.DisplayCode, [2]), Json)));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created); Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict); foreach (var r in responses) r.Dispose();
        var e = (await f.GetOrder(o.DisplayCode)).ExportSlips.Single(); var edit = new EditExportSlipRequest { RequestId = Guid.NewGuid(), Version = e.Version, FacilityName = "Edited", ItemNos = [2, 3] };
        var both = await Task.WhenAll(f.Staff.PutAsJsonAsync(Exports + "/" + e.DisplayCode, edit, Json), f.Staff.PostAsJsonAsync(Exports + "/" + e.DisplayCode + "/cancel", new CancelServiceOrderRequest { RequestId = Guid.NewGuid(), Version = e.Version, Reason = "Cancel" }, Json));
        Assert.Single(both, r => r.StatusCode == HttpStatusCode.NoContent); Assert.Single(both, r => r.StatusCode == HttpStatusCode.Conflict); foreach (var r in both) r.Dispose();
    }

    [PostgreSqlFact]
    public async Task Pending_cancel_releases_only_owned_guards_and_keeps_audit_and_source_hold()
    {
        await using var f = await Fixture.CreateAsync(); var o = await f.CreateOrder(); await f.Approve(o.DisplayCode); var a = await f.CreateExport(o.DisplayCode, [2]); var b = await f.CreateExport(o.DisplayCode, [3]);
        var cancel = new CancelServiceOrderRequest { RequestId = Guid.NewGuid(), Version = a.Version, Reason = "Facility closed" };
        await Expect(await f.Staff.PostAsJsonAsync(Exports + "/" + a.DisplayCode + "/cancel", cancel, Json), HttpStatusCode.NoContent);
        await Expect(await f.Staff.PostAsJsonAsync(Exports + "/" + a.DisplayCode + "/cancel", cancel, Json), HttpStatusCode.NoContent);
        Assert.Equal(ServiceWorkflowStatus.Canceled, (await f.GetExport(a.DisplayCode)).Status);
        await f.InDb(async db => { Assert.Equal(2, await db.ExportSlips.CountAsync()); Assert.Single(await db.Set<ExportDeviceGuard>().ToListAsync()); Assert.Equal(3, await db.Set<ServiceOrderDeviceHold>().CountAsync()); });
        await f.ApproveExport(b.DisplayCode);
        await Expect(await f.Staff.PostAsJsonAsync(Exports + "/" + b.DisplayCode + "/cancel", new CancelServiceOrderRequest { RequestId = Guid.NewGuid(), Reason = "Late" }, Json), HttpStatusCode.Conflict);
    }

    [PostgreSqlFact]
    public async Task Mixed_export_results_do_not_finalize_source_and_failed_item_can_retry()
    {
        await using var f = await Fixture.CreateAsync(); var o = await f.CreateOrder(); await f.Approve(o.DisplayCode); var e = await f.CreateExport(o.DisplayCode); await f.ApproveExport(e.DisplayCode);
        var request = new RecordExportResultRequest { RequestId = Guid.NewGuid(), Items = [new() { ItemNo = 2, Result = ExportResult.Success, RecipientName = "Receiver" }, new() { ItemNo = 3, Result = ExportResult.Failed, FailureReason = "Unavailable" }] };
        await f.ExportResults(e.DisplayCode, request); await f.ExportResults(e.DisplayCode, request);
        var finished = await f.GetExport(e.DisplayCode); Assert.Equal(ServiceWorkflowStatus.Completed, finished.Status); Assert.Equal(ExportResult.Failed, finished.Items.Single(i => i.ItemNo == 3).Result);
        Assert.All((await f.GetOrder(o.DisplayCode)).Items, i => Assert.Equal(ServiceItemStatus.InProgress, i.ExecutionStatus));
        await f.InDb(async db => { Assert.Equal(3, await db.Set<ServiceOrderDeviceHold>().CountAsync()); Assert.Equal(ExportGuardState.AwaitingReturn, (await db.Set<ExportDeviceGuard>().SingleAsync()).GuardState); });
        await f.CreateExport(o.DisplayCode, [3]);
        await Expect(await f.Staff.PostAsJsonAsync(Exports + "/" + e.DisplayCode + "/result", new RecordExportResultRequest { RequestId = request.RequestId, Items = [request.Items[0]] }, Json), HttpStatusCode.Conflict, "IDEMPOTENCY_CONFLICT");
    }

    [PostgreSqlFact]
    public async Task Professional_outcome_blocks_pending_export_and_retains_hold_until_return_and_reconciliation()
    {
        await using var f = await Fixture.CreateAsync(); var o = await f.CreateOrder(); await f.Approve(o.DisplayCode); var e = await f.CreateExport(o.DisplayCode, [2]);
        var result = new RecordServiceResultsRequest { RequestId = Guid.NewGuid(), Items = [new() { ItemNo = 2, Result = ServiceItemStatus.Completed, Description = "Done" }] };
        await Expect(await f.Staff.PostAsJsonAsync(Root + "/" + o.DisplayCode + "/results", result, Json), HttpStatusCode.Conflict, "EXPORT_RESULT_PENDING");
        await f.ApproveExport(e.DisplayCode); await f.ExportResults(e.DisplayCode, new() { RequestId = Guid.NewGuid(), Items = [new() { ItemNo = 2, Result = ExportResult.Success, RecipientName = "Workshop" }] });
        await f.Results(o.DisplayCode, [new() { ItemNo = 1, Result = ServiceItemStatus.Completed, Description = "Done" }, new() { ItemNo = 2, Result = ServiceItemStatus.Completed, Description = "Done" }, new() { ItemNo = 3, Result = ServiceItemStatus.NotExecuted, Description = "Not scheduled" }]);
        Assert.Equal(ServiceWorkflowStatus.Completed, (await f.GetOrder(o.DisplayCode)).Status);
        await f.InDb(async db => Assert.Single(await db.Set<ServiceOrderDeviceHold>().ToListAsync()));
        var returned = await f.CreateReturn(f.ReturnRequest(e.DisplayCode, [2]));
        Assert.Equal("Completed", returned.Status);
        await f.InDb(async db => { Assert.Single(await db.Set<ServiceOrderDeviceHold>().ToListAsync()); Assert.Empty(await db.Set<ExportDeviceGuard>().ToListAsync()); });
        var reconcile = new ServiceTransitionRequest { RequestId = Guid.NewGuid() };
        using var a = await f.Staff.PostAsJsonAsync(Root + "/" + o.DisplayCode + "/reconcile-holds", reconcile, Json); Assert.Equal(HttpStatusCode.OK, a.StatusCode); Assert.Equal(1, (await a.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("released").GetInt32());
        using var b = await f.Staff.PostAsJsonAsync(Root + "/" + o.DisplayCode + "/reconcile-holds", reconcile, Json); Assert.Equal(1, (await b.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("released").GetInt32());
        await f.InDb(async db => { Assert.Empty(await db.Set<ServiceOrderDeviceHold>().ToListAsync()); Assert.Equal(3, await db.ServiceOrderHistories.CountAsync(h => h.ActionCode == "HOLD_RELEASED")); Assert.All(await db.Devices.ToListAsync(), d => Assert.Equal(DeviceCondition.Good, d.Condition)); });
    }

    [PostgreSqlFact]
    public async Task Partial_returns_are_exactly_once_and_distinguish_actor_receiver_and_times()
    {
        await using var f = await Fixture.CreateAsync(); var o = await f.CreateOrder(); await f.Approve(o.DisplayCode); var e = await f.CreateExport(o.DisplayCode); await f.ApproveExport(e.DisplayCode);
        await f.ExportResults(e.DisplayCode, new() { RequestId = Guid.NewGuid(), Items = [new() { ItemNo = 2, Result = ExportResult.Success, RecipientName = "A" }, new() { ItemNo = 3, Result = ExportResult.Success, RecipientName = "B" }] });
        var request = f.ReturnRequest(e.DisplayCode, [2]); var first = await f.CreateReturn(request); var retry = await f.CreateReturn(request); Assert.Equal(first.DisplayCode, retry.DisplayCode);
        Assert.Equal("TK_MANAGER", first.ReceivedByAccountCode); Assert.Equal("TK_STAFF", first.CreatedByAccountCode); Assert.True(first.ReceivedAt < first.CreatedAt);
        await Expect(await f.Staff.PostAsJsonAsync("/api/v1/export-return-slips", f.ReturnRequest(e.DisplayCode, [2]), Json), HttpStatusCode.Conflict);
        var second = await f.CreateReturn(f.ReturnRequest(e.DisplayCode, [3])); Assert.Equal(2, second.SequenceNo);
        Assert.All((await f.GetExport(e.DisplayCode)).Items, i =>
        {
            Assert.True(i.Returned); Assert.NotNull(i.ReturnSlipCode); Assert.NotNull(i.ReceivedAt);
            Assert.Equal("TK_MANAGER", i.ReceivedByAccountCode); Assert.Equal(DeviceCondition.Damaged, i.ConditionAtReturn); Assert.Null(i.GuardState);
        });
        await f.InDb(async db => { Assert.Equal(2, await db.Set<ExportReturnSlip>().CountAsync()); Assert.Empty(await db.Set<ExportDeviceGuard>().ToListAsync()); Assert.Equal(3, await db.Set<ServiceOrderDeviceHold>().CountAsync()); Assert.All(await db.ServiceOrderItems.ToListAsync(), i => Assert.Equal(ServiceItemStatus.InProgress, i.ExecutionStatus)); });
        var history = await f.Staff.GetFromJsonAsync<JsonElement>("/api/v1/export-return-slips/" + first.DisplayCode + "/history"); Assert.Equal(2, history.GetProperty("totalCount").GetInt32());
        using var page = await f.Staff.GetAsync("/api/v1/export-return-slips?exportSlipCode=" + e.DisplayCode); Assert.Equal(HttpStatusCode.OK, page.StatusCode);
    }

    [PostgreSqlFact]
    public async Task Competing_returns_have_one_winner_and_no_duplicate_release()
    {
        await using var f = await Fixture.CreateAsync(); var o = await f.CreateOrder(); await f.Approve(o.DisplayCode); var e = await f.CreateExport(o.DisplayCode, [2]); await f.ApproveExport(e.DisplayCode); await f.ExportResults(e.DisplayCode, new() { RequestId = Guid.NewGuid(), Items = [new() { ItemNo = 2, Result = ExportResult.Success, RecipientName = "A" }] });
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => f.Staff.PostAsJsonAsync("/api/v1/export-return-slips", f.ReturnRequest(e.DisplayCode, [2]), Json)));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created); Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict); foreach (var r in responses) r.Dispose();
        await f.InDb(async db => { Assert.Single(await db.Set<ExportReturnSlip>().ToListAsync()); Assert.Single(await db.Set<ExportReturnSlipItem>().ToListAsync()); Assert.Equal(2, await db.Set<ExportReturnHistory>().CountAsync()); });
    }

    [PostgreSqlFact]
    public async Task Return_rejects_future_time_bad_condition_mixed_source_and_changed_retry()
    {
        await using var f = await Fixture.CreateAsync(); var o = await f.CreateOrder(); await f.Approve(o.DisplayCode); var a = await f.CreateExport(o.DisplayCode, [2]); var b = await f.CreateExport(o.DisplayCode, [3]); await f.ApproveExport(a.DisplayCode); await f.ApproveExport(b.DisplayCode);
        await f.ExportResults(a.DisplayCode, new() { RequestId = Guid.NewGuid(), Items = [new() { ItemNo = 2, Result = ExportResult.Success, RecipientName = "A" }] });
        await Expect(await f.Staff.PostAsJsonAsync("/api/v1/export-return-slips", f.ReturnRequest(a.DisplayCode, [2, 3]), Json), HttpStatusCode.BadRequest);
        var request = f.ReturnRequest(a.DisplayCode, [2]);
        await Expect(await f.Staff.PostAsJsonAsync("/api/v1/export-return-slips", new CreateExportReturnRequest { RequestId = Guid.NewGuid(), ExportSlipCode = a.DisplayCode, HandoverPersonName = "A", ReceivedByAccountCode = "TK_MANAGER", ReceivedAt = f.Host.Clock.GetUtcNow().AddSeconds(1), Items = request.Items }, Json), HttpStatusCode.BadRequest);
        await f.CreateReturn(request);
        await Expect(await f.Staff.PostAsJsonAsync("/api/v1/export-return-slips", new CreateExportReturnRequest { RequestId = request.RequestId, ExportSlipCode = a.DisplayCode, HandoverPersonName = "Different", ReceivedByAccountCode = request.ReceivedByAccountCode, ReceivedAt = request.ReceivedAt, Items = request.Items }, Json), HttpStatusCode.Conflict, "IDEMPOTENCY_CONFLICT");
    }

    [PostgreSqlFact]
    public async Task Database_guards_enforce_completion_release_and_immutable_returns()
    {
        await using var f = await Fixture.CreateAsync(); var o = await f.CreateOrder(); await f.Approve(o.DisplayCode); var e = await f.CreateExport(o.DisplayCode, [2]);
        await f.InDb(async db =>
        {
            await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM service_order_device_holds"));
            await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM export_device_guards"));
            await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE export_slips SET status='Completed', completed_at=now()"));
            await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM service_order_history"));
            Assert.False(db.Database.HasPendingModelChanges());
        });
        await f.ApproveExport(e.DisplayCode); await f.ExportResults(e.DisplayCode, new() { RequestId = Guid.NewGuid(), Items = [new() { ItemNo = 2, Result = ExportResult.Success, RecipientName = "A" }] }); await f.CreateReturn(f.ReturnRequest(e.DisplayCode, [2]));
        await f.InDb(async db =>
        {
            await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE export_return_slips SET notes='changed'"));
            await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM export_return_slip_items"));
            await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE export_slip_items SET result='Failed'"));
        });
    }

    [PostgreSqlFact]
    public async Task Migration_can_down_up_when_empty_and_fails_closed_on_legacy_data()
    {
        await using var f = await Fixture.CreateAsync();
        await f.InDb(async db =>
        {
            var migrator = Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>(db); await migrator.MigrateAsync("20261103223600_DamageReportsGroup14");
            await migrator.MigrateAsync(); Assert.False(db.Database.HasPendingModelChanges()); Assert.Equal(3, await db.Devices.CountAsync());
            await migrator.MigrateAsync("20261103223600_DamageReportsGroup14");
            var id = Guid.NewGuid(); var request = Guid.NewGuid(); var creator = await db.Accounts.SingleAsync(a => a.AccountCode == "TK_STAFF");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO service_orders(id,order_type,display_code,create_request_id,create_request_hash,status,created_by_account_id,created_at,version) VALUES ({id},'Maintenance','BT_LEGACY',{request},{new string('A', 64)},'PendingApproval',{creator.Id},{f.Host.Clock.GetUtcNow()},0)");
            var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync());
            Assert.Contains("empty legacy", error.MessageText); Assert.Equal(1, await db.ServiceOrders.CountAsync());
            Assert.Equal(3, await db.Devices.CountAsync());
        });
    }

    [PostgreSqlTheory]
    [InlineData("CREATE")]
    [InlineData("EDIT")]
    [InlineData("CANCEL")]
    [InlineData("APPROVE")]
    [InlineData("ITEM_RESULT_RECORDED")]
    [InlineData("RETURN_SLIP_RECORDED")]
    public async Task Export_and_return_audit_failure_rolls_back_document_items_and_guards(string action)
    {
        await using var f = await Fixture.CreateAsync();
        var order = await f.CreateOrder(); await f.Approve(order.DisplayCode);
        if (action == "CREATE")
        {
            f.Host.Failures.FailNextExportAction = action;
            await Expect(await f.Staff.PostAsJsonAsync(Exports, f.ExportRequest(order.DisplayCode), Json), HttpStatusCode.ServiceUnavailable);
            await f.InDb(async db => { Assert.Empty(await db.ExportSlips.ToListAsync()); Assert.Empty(await db.Set<ExportDeviceGuard>().ToListAsync()); });
            return;
        }
        var export = await f.CreateExport(order.DisplayCode);
        HttpResponseMessage response;
        if (action == "EDIT")
        {
            f.Host.Failures.FailNextExportAction = action;
            response = await f.Staff.PutAsJsonAsync(Exports + "/" + export.DisplayCode,
                new EditExportSlipRequest { RequestId = Guid.NewGuid(), Version = export.Version, FacilityName = "Changed", ItemNos = [2] }, Json);
        }
        else if (action == "CANCEL")
        {
            f.Host.Failures.FailNextExportAction = action;
            response = await f.Staff.PostAsJsonAsync(Exports + "/" + export.DisplayCode + "/cancel",
                new CancelServiceOrderRequest { RequestId = Guid.NewGuid(), Reason = "Not needed" }, Json);
        }
        else if (action == "APPROVE")
        {
            f.Host.Failures.FailNextExportAction = action;
            response = await f.Manager.PostAsJsonAsync(Exports + "/" + export.DisplayCode + "/approve", new ServiceTransitionRequest { RequestId = Guid.NewGuid() }, Json);
        }
        else
        {
            await f.ApproveExport(export.DisplayCode);
            var result = new RecordExportResultRequest { RequestId = Guid.NewGuid(), Items = [new() { ItemNo = 2, Result = ExportResult.Success, RecipientName = "A" }, new() { ItemNo = 3, Result = ExportResult.Failed, FailureReason = "Closed" }] };
            if (action == "ITEM_RESULT_RECORDED")
            {
                f.Host.Failures.FailNextExportAction = action;
                response = await f.Staff.PostAsJsonAsync(Exports + "/" + export.DisplayCode + "/result", result, Json);
            }
            else
            {
                await f.ExportResults(export.DisplayCode, result);
                f.Host.Failures.FailNextReturnAction = action;
                response = await f.Staff.PostAsJsonAsync("/api/v1/export-return-slips", f.ReturnRequest(export.DisplayCode, [2]), Json);
            }
        }
        await Expect(response, HttpStatusCode.ServiceUnavailable);
        var current = await f.GetExport(export.DisplayCode);
        Assert.Equal("Workshop", current.FacilityName); Assert.Equal(2, current.Items.Count);
        await f.InDb(async db =>
        {
            Assert.Equal(3, await db.Set<ServiceOrderDeviceHold>().CountAsync());
            Assert.Empty(await db.Set<ExportReturnSlip>().ToListAsync());
            Assert.Empty(await db.Set<ExportReturnHistory>().ToListAsync());
            Assert.Equal(action == "RETURN_SLIP_RECORDED" ? 1 : 2, await db.Set<ExportDeviceGuard>().CountAsync());
            if (action != "RETURN_SLIP_RECORDED") Assert.All(current.Items, i => Assert.Null(i.Result));
        });
    }

    [PostgreSqlFact]
    public async Task Successful_return_allows_a_new_export_while_source_item_remains_in_progress()
    {
        await using var f = await Fixture.CreateAsync();
        var order = await f.CreateOrder(); await f.Approve(order.DisplayCode);
        var first = await f.CreateExport(order.DisplayCode, [2]); await f.ApproveExport(first.DisplayCode);
        await f.ExportResults(first.DisplayCode, new() { RequestId = Guid.NewGuid(), Items = [new() { ItemNo = 2, Result = ExportResult.Success, RecipientName = "A" }] });
        await f.CreateReturn(f.ReturnRequest(first.DisplayCode, [2]));
        var next = await f.CreateExport(order.DisplayCode, [2]);
        Assert.Equal(2, next.SequenceNo); Assert.NotEqual(first.DisplayCode, next.DisplayCode);
        Assert.True(Assert.Single((await f.GetExport(first.DisplayCode)).Items).Returned);
        Assert.Null(Assert.Single(next.Items).Result);
        await f.InDb(async db => { Assert.Equal(3, await db.Set<ServiceOrderDeviceHold>().CountAsync()); Assert.Single(await db.Set<ExportDeviceGuard>().ToListAsync()); });
    }

    [PostgreSqlFact]
    public async Task Professional_result_after_return_releases_hold_and_failed_audit_rolls_back_release()
    {
        await using var f = await Fixture.CreateAsync();
        var order = await f.CreateOrder(); await f.Approve(order.DisplayCode);
        var export = await f.CreateExport(order.DisplayCode, [2]); await f.ApproveExport(export.DisplayCode);
        await f.ExportResults(export.DisplayCode, new() { RequestId = Guid.NewGuid(), Items = [new() { ItemNo = 2, Result = ExportResult.Success, RecipientName = "A" }] });
        await f.CreateReturn(f.ReturnRequest(export.DisplayCode, [2]));
        var result = new RecordServiceResultsRequest { RequestId = Guid.NewGuid(), Version = 1, Items = [new() { ItemNo = 2, Result = ServiceItemStatus.Failed, Description = "Cannot repair" }] };
        f.Host.Failures.FailNextServiceAction = "HOLD_RELEASED";
        await Expect(await f.Staff.PostAsJsonAsync(Root + "/" + order.DisplayCode + "/results", result, Json), HttpStatusCode.ServiceUnavailable);
        await f.InDb(async db => { Assert.Equal(3, await db.Set<ServiceOrderDeviceHold>().CountAsync()); Assert.Empty(await db.ServiceOrderHistories.Where(h => h.ActionCode == "HOLD_RELEASED").ToListAsync()); });
        await Expect(await f.Staff.PostAsJsonAsync(Root + "/" + order.DisplayCode + "/results", result, Json), HttpStatusCode.NoContent);
        await Expect(await f.Staff.PostAsJsonAsync(Root + "/" + order.DisplayCode + "/results", result, Json), HttpStatusCode.NoContent);
        await f.InDb(async db => Assert.Equal(2, await db.Set<ServiceOrderDeviceHold>().CountAsync()));
    }

    [PostgreSqlFact]
    public async Task Reconciliation_without_eligible_holds_is_a_durable_no_change()
    {
        await using var f = await Fixture.CreateAsync(); var order = await f.CreateOrder(); await f.Approve(order.DisplayCode);
        var request = new ServiceTransitionRequest { RequestId = Guid.NewGuid() };
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var response = await f.Staff.PostAsJsonAsync(Root + "/" + order.DisplayCode + "/reconcile-holds", request, Json);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(0, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("released").GetInt32());
        }
        await f.InDb(async db => { Assert.Equal(3, await db.Set<ServiceOrderDeviceHold>().CountAsync()); Assert.Single(await db.ServiceOrderHistories.Where(h => h.ActionCode == "HOLDS_RECONCILED").ToListAsync()); });
    }

    [PostgreSqlFact]
    public async Task Noop_edit_keeps_version_and_persists_retry_hash()
    {
        await using var f = await Fixture.CreateAsync(); var order = await f.CreateOrder(); await f.Approve(order.DisplayCode);
        var export = await f.CreateExport(order.DisplayCode);
        var request = new EditExportSlipRequest { RequestId = Guid.NewGuid(), Version = export.Version, FacilityName = export.FacilityName, ItemNos = [2, 3] };
        await Expect(await f.Staff.PutAsJsonAsync(Exports + "/" + export.DisplayCode, request, Json), HttpStatusCode.NoContent);
        await Expect(await f.Staff.PutAsJsonAsync(Exports + "/" + export.DisplayCode, request, Json), HttpStatusCode.NoContent);
        Assert.Equal(export.Version, (await f.GetExport(export.DisplayCode)).Version);
        await f.InDb(async db => Assert.Equal(AuditOutcome.NoChange, (await db.ExportHistories.SingleAsync(h => h.ActionCode == "EDIT")).Outcome));
        await Expect(await f.Staff.PutAsJsonAsync(Exports + "/" + export.DisplayCode,
            new EditExportSlipRequest { RequestId = request.RequestId, Version = request.Version, FacilityName = "Changed", ItemNos = [2, 3] }, Json), HttpStatusCode.Conflict, "IDEMPOTENCY_CONFLICT");
    }

    [PostgreSqlFact]
    public async Task Permission_uses_active_session_role_and_current_equipment_department()
    {
        await using var f = await Fixture.CreateAsync();
        await f.InDb(async db =>
        {
            var other = new CSVC.Core.Departments.Department { Code = "PB_MOVED", Name = "Moved" }; db.Add(other);
            var account = await db.Accounts.SingleAsync(a => a.AccountCode == "TK_STAFF");
            (await db.UserProfiles.SingleAsync(p => p.Id == account.UserProfileId)).DepartmentId = other.Id;
            await db.SaveChangesAsync();
        });
        await Expect(await f.Staff.GetAsync(Root), HttpStatusCode.Forbidden, "EQUIPMENT_ACCOUNT_DEPARTMENT_MISMATCH");
        await Expect(await f.Staff.PostAsJsonAsync(Root, f.Request(), Json), HttpStatusCode.Forbidden);
    }

    [PostgreSqlFact]
    public async Task Repair_optional_report_link_requires_same_device_and_does_not_change_report()
    {
        await using var f = await Fixture.CreateAsync();
        using var created = await f.Staff.PostAsJsonAsync("/api/v1/damage-reports", new
        {
            requestId = Guid.NewGuid(), deviceCode = "TB_SERVICE_1", incidentType = "DAMAGE",
            incidentDescription = "Fails intermittently", detectedAt = f.Host.Clock.GetUtcNow().AddMinutes(-5)
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var report = await created.Content.ReadFromJsonAsync<JsonElement>(); var code = report.GetProperty("reportCode").GetString();
        var request = new CreateServiceOrderRequest { RequestId = Guid.NewGuid(), OrderType = ServiceOrderType.Repair,
            Items = [new() { DeviceCode = "TB_SERVICE_1", ExecutionMethod = ExecutionMethod.OnSite, DamageReportCode = code }] };
        var source = await f.CreateOrder(request); Assert.Equal(code, source.Items.Single().DamageReportCode);
        Assert.Equal(code, (await f.GetOrder(source.DisplayCode)).Items.Single().DamageReportCode);
        await Expect(await f.Staff.PostAsJsonAsync(Root, new CreateServiceOrderRequest { RequestId = Guid.NewGuid(), OrderType = ServiceOrderType.Repair,
            Items = [new() { DeviceCode = "TB_SERVICE_2", ExecutionMethod = ExecutionMethod.OnSite, DamageReportCode = code }] }, Json), HttpStatusCode.BadRequest);
        await f.InDb(async db => { var original = await db.DamageReports.SingleAsync(); Assert.Equal(1, original.RowVersion); Assert.Null(original.LastUpdatedAt); Assert.Single(await db.RepairItemDetails.ToListAsync()); });
    }

    [PostgreSqlFact]
    public async Task Composite_foreign_keys_reject_wrong_source_hold_and_mixed_return_parent()
    {
        await using var f = await Fixture.CreateAsync();
        var first = await f.CreateOrder(); var other = await f.CreateOrder();
        await f.InDb(async db =>
        {
            var source = await db.ServiceOrders.SingleAsync(o => o.DisplayCode == first.DisplayCode);
            var foreign = await db.ServiceOrders.SingleAsync(o => o.DisplayCode == other.DisplayCode);
            var item = await db.ServiceOrderItems.SingleAsync(i => i.ServiceOrderId == foreign.Id && i.ItemNo == 2);
            var actor = await db.Accounts.SingleAsync(a => a.AccountCode == "TK_MANAGER");
            var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO service_order_device_holds(device_id,service_order_id,service_order_item_id,held_by_account_id,held_at,version) VALUES ({item.DeviceId},{source.Id},{item.Id},{actor.Id},{f.Host.Clock.GetUtcNow()},1)"));
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
        });
        await f.Approve(first.DisplayCode);
        var a = await f.CreateExport(first.DisplayCode, [2]); var b = await f.CreateExport(first.DisplayCode, [3]);
        await f.ApproveExport(a.DisplayCode); await f.ApproveExport(b.DisplayCode);
        await f.ExportResults(a.DisplayCode, new() { RequestId = Guid.NewGuid(), Items = [new() { ItemNo = 2, Result = ExportResult.Success, RecipientName = "A" }] });
        await f.ExportResults(b.DisplayCode, new() { RequestId = Guid.NewGuid(), Items = [new() { ItemNo = 3, Result = ExportResult.Success, RecipientName = "B" }] });
        await f.CreateReturn(f.ReturnRequest(a.DisplayCode, [2]));
        await f.InDb(async db =>
        {
            var header = await db.Set<ExportReturnSlip>().SingleAsync();
            var export = await db.ExportSlips.SingleAsync(e => e.DisplayCode == b.DisplayCode);
            var line = await db.ExportSlipItems.SingleAsync(l => l.ExportSlipId == export.Id);
            var id = Guid.NewGuid();
            var headerError = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO export_return_slip_items(id,return_slip_id,export_slip_id,export_slip_item_id,device_code_snapshot,device_name_snapshot,condition_at_return) VALUES ({id},{header.Id},{export.Id},{line.Id},'TEST','TEST','GOOD')"));
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, headerError.SqlState);
            var itemError = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO export_return_slip_items(id,return_slip_id,export_slip_id,export_slip_item_id,device_code_snapshot,device_name_snapshot,condition_at_return) VALUES ({id},{header.Id},{header.ExportSlipId},{line.Id},'TEST','TEST','GOOD')"));
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, itemError.SqlState);
        });
    }

    private static async Task Expect(HttpResponseMessage response, HttpStatusCode status, string? code = null)
    {
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == status, $"Expected {(int)status}, received {(int)response.StatusCode}: {body}");
            if (code is not null) Assert.Equal(code, JsonDocument.Parse(body).RootElement.GetProperty("code").GetString());
        }
    }

    private sealed class Fixture(AccountTestDatabase database, AccountApiTestHost host, HttpClient staff, HttpClient manager, Guid department) : IAsyncDisposable
    {
        public AccountApiTestHost Host { get; } = host;
        public HttpClient Staff { get; } = staff;
        public HttpClient Manager { get; } = manager;
        public Guid Department { get; } = department;
        public static async Task<Fixture> CreateAsync()
        {
            var db = await AccountTestDatabase.CreateAsync();
            var host = new AccountApiTestHost(db.ConnectionString);
            try
            {
                var seed = await host.SeedAsync();
                var staff = host.CreateClient(); var manager = host.CreateClient();
                staff.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", seed.StaffToken);
                manager.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", seed.ManagerToken);
                var f = new Fixture(db, host, staff, manager, seed.Department);
                await f.InDb(async context =>
                {
                    var settings = await context.EquipmentSettings.SingleAsync(); settings.EquipmentDepartmentId = seed.Department;
                    var category = new DeviceCategory { DisplayCode = "CAT_SERVICE", Name = "Service test" };
                    context.Add(category);
                    foreach (var n in Enumerable.Range(1, 3))
                    {
                        context.Add(new DeviceIdentity { DeviceCode = "TB_SERVICE_" + n, Barcode = "BC_SERVICE_" + n, ReservedAt = host.Clock.GetUtcNow() });
                        context.Add(new Device
                        {
                            DeviceCode = "TB_SERVICE_" + n,
                            Barcode = "BC_SERVICE_" + n,
                            Name = "Device " + n,
                            Quantity = 1,
                            CategoryId = category.Id,
                            DepartmentId = seed.Department,
                            ResponsibleAccountId = seed.Staff
                        });
                    }
                    await context.SaveChangesAsync();
                });
                return f;
            }
            catch { await host.DisposeAsync(); await db.DisposeAsync(); throw; }
        }
        public CreateServiceOrderRequest Request(ServiceOrderType type = ServiceOrderType.Maintenance) => new()
        {
            RequestId = Guid.NewGuid(),
            OrderType = type,
            Items = Enumerable.Range(1, 3).Select(n => new ServiceItemRequest
            {
                DeviceCode = "TB_SERVICE_" + n,
                ExecutionMethod = n == 1 ? ExecutionMethod.OnSite : ExecutionMethod.External,
                WorkDescription = "Check device"
            }).ToList()
        };
        public CreateExportSlipRequest ExportRequest(string code, List<int>? items = null) => new() { RequestId = Guid.NewGuid(), ServiceOrderCode = code, FacilityName = "Workshop", ItemNos = items ?? [2, 3] };
        public CreateExportReturnRequest ReturnRequest(string code, List<int> items) => new() { RequestId = Guid.NewGuid(), ExportSlipCode = code, HandoverPersonName = "Courier", ReceivedByAccountCode = "TK_MANAGER", ReceivedAt = Host.Clock.GetUtcNow().AddHours(-1), Items = items.Select(n => new ReturnItemRequest { ItemNo = n, ConditionAtReturn = DeviceCondition.Damaged }).ToList() };
        public async Task<ExportReturnDto> CreateReturn(CreateExportReturnRequest request) { using var response = await Staff.PostAsJsonAsync("/api/v1/export-return-slips", request, Json); var text = await response.Content.ReadAsStringAsync(); Assert.True(response.StatusCode == HttpStatusCode.Created, text); return JsonSerializer.Deserialize<ExportReturnDto>(text, Json)!; }
        public async Task<ExportSlipDto> CreateExport(string code, List<int>? items = null) { using var response = await Staff.PostAsJsonAsync(Exports, ExportRequest(code, items), Json); var text = await response.Content.ReadAsStringAsync(); Assert.True(response.StatusCode == HttpStatusCode.Created, text); return JsonSerializer.Deserialize<ExportSlipDto>(text, Json)!; }
        public async Task<ExportSlipDto> GetExport(string code) => (await Staff.GetFromJsonAsync<ExportSlipDto>(Exports + "/" + code, Json))!;
        public async Task ApproveExport(string code) => await Expect(await Manager.PostAsJsonAsync(Exports + "/" + code + "/approve", new ServiceTransitionRequest { RequestId = Guid.NewGuid() }, Json), HttpStatusCode.NoContent);
        public async Task ExportResults(string code, RecordExportResultRequest request) => await Expect(await Staff.PostAsJsonAsync(Exports + "/" + code + "/result", request, Json), HttpStatusCode.NoContent);
        public async Task<ServiceOrderDto> CreateOrder(CreateServiceOrderRequest? request = null)
        {
            using var response = await Staff.PostAsJsonAsync(Root, request ?? Request(), Json);
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.Created, body);
            return JsonSerializer.Deserialize<ServiceOrderDto>(body, Json)!;
        }
        public async Task Approve(string code, ServiceTransitionRequest? request = null)
            => await Expect(await Manager.PostAsJsonAsync(Root + "/" + code + "/approve", request ?? new() { RequestId = Guid.NewGuid() }, Json), HttpStatusCode.NoContent);
        public async Task Results(string code, List<ServiceItemResultRequest> items)
            => await Expect(await Staff.PostAsJsonAsync(Root + "/" + code + "/results", new RecordServiceResultsRequest { RequestId = Guid.NewGuid(), Items = items }, Json), HttpStatusCode.NoContent);
        public async Task<ServiceOrderDto> GetOrder(string code) => (await Staff.GetFromJsonAsync<ServiceOrderDto>(Root + "/" + code, Json))!;
        public async Task InDb(Func<CsvcDbContext, Task> action)
        {
            await using var scope = Host.Services.CreateAsyncScope();
            await action(scope.ServiceProvider.GetRequiredService<CsvcDbContext>());
        }
        public async ValueTask DisposeAsync()
        {
            Staff.Dispose(); Manager.Dispose(); await Host.DisposeAsync(); await database.DisposeAsync();
        }
    }
}

public sealed class PostgreSqlTheoryAttribute : TheoryAttribute
{
    public PostgreSqlTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CSVC_POSTGRES_TEST_CONNECTION")))
            Skip = "Set CSVC_POSTGRES_TEST_CONNECTION to an isolated PostgreSQL test server.";
    }
}
