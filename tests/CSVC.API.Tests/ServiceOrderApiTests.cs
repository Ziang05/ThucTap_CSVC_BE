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
