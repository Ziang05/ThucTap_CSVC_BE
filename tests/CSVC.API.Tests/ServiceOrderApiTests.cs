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
    public async Task Create_is_atomic_snapshot_based_and_idempotent_with_safe_business_identifiers()
    {
        await using var f = await Fixture.CreateAsync();
        var request = f.Request();
        var order = await f.CreateOrder(request);
        Assert.Equal(32, order.DisplayCode.Length);
        Assert.StartsWith("BT", order.DisplayCode);
        Assert.Equal(ServiceWorkflowStatus.PendingApproval, order.Status);
        Assert.All(order.Items, i => Assert.Null(i.ExecutionStatus));
        Assert.Single(order.ExportSlips);
        Assert.Equal([2, 3], order.ExportSlips[0].Items.Select(x => x.ItemNo));
        var retry = await f.CreateOrder(request);
        Assert.Equal(order.DisplayCode, retry.DisplayCode);
        await f.InDb(async db =>
        {
            Assert.Single(await db.ServiceOrders.ToListAsync());
            Assert.Single(await db.ExportSlips.ToListAsync());
            Assert.Single(await db.ServiceOrderHistories.ToListAsync());
            Assert.Empty(await db.DeviceOperationHolds.ToListAsync());
            var device = await db.Devices.SingleAsync(x => x.DeviceCode == "TB_SERVICE_1");
            device.Name = "Changed after creation";
            await db.SaveChangesAsync();
        });
        var read = await f.GetOrder(order.DisplayCode);
        Assert.Equal("Device 1", read.Items[0].DeviceName);
        var conflicting = new CreateServiceOrderRequest { RequestId = request.RequestId, Items = request.Items, ExportSlips = request.ExportSlips, Notes = "different" };
        await Expect(await f.Staff.PostAsJsonAsync(Root, conflicting, Json), HttpStatusCode.Conflict, "IDEMPOTENCY_CONFLICT");
        var page = await f.Staff.GetFromJsonAsync<JsonElement>(Root + "?orderType=Maintenance&pageSize=1");
        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
        var exportPage = await f.Staff.GetFromJsonAsync<JsonElement>(Exports + "?serviceOrderCode=" + order.DisplayCode);
        Assert.Equal(1, exportPage.GetProperty("totalCount").GetInt32());
        var body = await f.Staff.GetStringAsync(Root + "/" + order.DisplayCode);
        Assert.DoesNotContain("serviceOrderId", body);
        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
    }

    [PostgreSqlTheory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("onsite")]
    [InlineData("unknown")]
    public async Task Export_membership_rejects_missing_duplicate_onsite_and_foreign_items(string kind)
    {
        await using var f = await Fixture.CreateAsync();
        var members = kind switch
        {
            "missing" => new[] { "TB_SERVICE_2" },
            "duplicate" => new[] { "TB_SERVICE_2", "TB_SERVICE_2", "TB_SERVICE_3" },
            "onsite" => new[] { "TB_SERVICE_1", "TB_SERVICE_2", "TB_SERVICE_3" },
            _ => new[] { "TB_UNKNOWN", "TB_SERVICE_2", "TB_SERVICE_3" }
        };
        var original = f.Request();
        var request = new CreateServiceOrderRequest { RequestId = Guid.NewGuid(), Items = original.Items,
            ExportSlips = [new() { FacilityName = "External workshop", DeviceCodes = members.ToList() }] };
        await Expect(await f.Staff.PostAsJsonAsync(Root, request, Json), HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        await f.InDb(async db => Assert.Empty(await db.ServiceOrders.ToListAsync()));
    }

    [PostgreSqlFact]
    public async Task Approval_reserves_every_device_and_enforces_role_version_and_terminal_guards()
    {
        await using var f = await Fixture.CreateAsync();
        var order = await f.CreateOrder();
        var request = new ServiceTransitionRequest { RequestId = Guid.NewGuid(), Version = 0 };
        await Expect(await f.Staff.PostAsJsonAsync(Root + "/" + order.DisplayCode + "/approve", request, Json), HttpStatusCode.Forbidden);
        await Expect(await f.Manager.PostAsJsonAsync(Root + "/" + order.DisplayCode + "/approve",
            new ServiceTransitionRequest { RequestId = Guid.NewGuid(), Version = 99 }, Json), HttpStatusCode.Conflict, "VERSION_CONFLICT");
        await f.Approve(order.DisplayCode, request);
        await f.Approve(order.DisplayCode, request);
        var approved = await f.GetOrder(order.DisplayCode);
        Assert.Equal(ServiceWorkflowStatus.InProgress, approved.Status);
        Assert.Equal(ServiceWorkflowStatus.InProgress, approved.ExportSlips[0].Status);
        Assert.All(approved.Items, x => Assert.Equal(ServiceItemStatus.InProgress, x.ExecutionStatus));
        await f.InDb(async db => Assert.Equal(3, await db.DeviceOperationHolds.CountAsync()));
        await Expect(await f.Staff.PostAsJsonAsync(Root + "/" + order.DisplayCode + "/cancel",
            new CancelServiceOrderRequest { RequestId = Guid.NewGuid(), Reason = "Too late" }, Json), HttpStatusCode.Conflict);
        await Expect(await f.Manager.PostAsJsonAsync(Exports + "/" + order.ExportSlips[0].DisplayCode + "/result",
            new RecordExportResultRequest { RequestId = Guid.NewGuid(), Result = ExportResult.Failed, FailureReason = "Unavailable" }, Json), HttpStatusCode.Forbidden);
    }

    [PostgreSqlFact]
    public async Task Failed_export_marks_only_its_items_not_executed_and_completes_parent_after_remaining_result()
    {
        await using var f = await Fixture.CreateAsync();
        var order = await f.CreateOrder();
        await f.Approve(order.DisplayCode);
        var slip = order.ExportSlips[0].DisplayCode;
        var fail = new RecordExportResultRequest { RequestId = Guid.NewGuid(), Result = ExportResult.Failed, FailureReason = "Workshop declined" };
        await Expect(await f.Staff.PostAsJsonAsync(Exports + "/" + slip + "/result", fail, Json), HttpStatusCode.NoContent);
        await Expect(await f.Staff.PostAsJsonAsync(Exports + "/" + slip + "/result", fail, Json), HttpStatusCode.NoContent);
        var current = await f.GetOrder(order.DisplayCode);
        Assert.Equal(ServiceWorkflowStatus.InProgress, current.Status);
        Assert.Equal(ServiceItemStatus.InProgress, current.Items[0].ExecutionStatus);
        Assert.All(current.Items.Skip(1), i => { Assert.Equal(ServiceItemStatus.NotExecuted, i.ExecutionStatus); Assert.Null(i.ResultDescription); });
        await f.InDb(async db =>
        {
            Assert.Single(await db.DeviceOperationHolds.ToListAsync());
            var events = await db.ServiceOrderHistories.Where(x => x.ActionCode == "ITEM_NOT_EXECUTED").ToListAsync();
            Assert.Equal(2, events.Count);
            Assert.All(events, x => Assert.NotNull(x.RelatedExportSlipId));
        });
        await f.Results(order.DisplayCode, [new() { ItemNo = 1, Result = ServiceItemStatus.Completed, Description = "Serviced" }]);
        current = await f.GetOrder(order.DisplayCode);
        Assert.Equal(ServiceWorkflowStatus.Completed, current.Status);
        Assert.NotNull(current.CompletedAt);
        var timeline = await f.Staff.GetFromJsonAsync<JsonElement>(Root + "/" + order.DisplayCode + "/history");
        Assert.Contains(timeline.GetProperty("items").EnumerateArray(), x => x.GetProperty("relatedExportSlipCode").GetString() == slip);
        await f.InDb(async db => Assert.Empty(await db.DeviceOperationHolds.ToListAsync()));
    }

    [PostgreSqlFact]
    public async Task Successful_export_keeps_items_in_progress_until_professional_results_and_bulk_retry_is_safe()
    {
        await using var f = await Fixture.CreateAsync();
        var order = await f.CreateOrder(f.Request(ServiceOrderType.Repair));
        await f.InDb(async db => Assert.Equal(3, await db.RepairItemDetails.CountAsync()));
        await f.Approve(order.DisplayCode);
        await Expect(await f.Staff.PostAsJsonAsync(Root + "/" + order.DisplayCode + "/results", new RecordServiceResultsRequest
        {
            RequestId = Guid.NewGuid(), Items = [new() { ItemNo = 2, Result = ServiceItemStatus.Completed, Description = "Too early" }]
        }, Json), HttpStatusCode.Conflict, "EXPORT_NOT_SUCCESSFUL");
        var slip = order.ExportSlips[0].DisplayCode;
        var success = new RecordExportResultRequest { RequestId = Guid.NewGuid(), Result = ExportResult.Success, RecipientName = "Workshop technician" };
        await Expect(await f.Staff.PostAsJsonAsync(Exports + "/" + slip + "/result", success, Json), HttpStatusCode.NoContent);
        var exported = await f.GetOrder(order.DisplayCode);
        Assert.All(exported.Items, x => Assert.Equal(ServiceItemStatus.InProgress, x.ExecutionStatus));
        Assert.Equal(ServiceWorkflowStatus.InProgress, exported.Status);
        await f.InDb(async db => Assert.Equal(3, await db.DeviceOperationHolds.CountAsync()));
        var request = new RecordServiceResultsRequest { RequestId = Guid.NewGuid(), Items =
        [new() { ItemNo = 1, Result = ServiceItemStatus.Completed, Description = "Done on site" },
         new() { ItemNo = 2, Result = ServiceItemStatus.Completed, Description = "Repaired" },
         new() { ItemNo = 3, Result = ServiceItemStatus.Failed, Description = "Irreparable" }] };
        await Expect(await f.Staff.PostAsJsonAsync(Root + "/" + order.DisplayCode + "/results", request, Json), HttpStatusCode.NoContent);
        await Expect(await f.Staff.PostAsJsonAsync(Root + "/" + order.DisplayCode + "/results", request, Json), HttpStatusCode.NoContent);
        var completed = await f.GetOrder(order.DisplayCode);
        Assert.Equal(ServiceWorkflowStatus.Completed, completed.Status);
        Assert.Equal(ServiceItemStatus.Failed, completed.Items[2].ExecutionStatus);
        await f.InDb(async db =>
        {
            Assert.Empty(await db.DeviceOperationHolds.ToListAsync());
            Assert.Equal(2, await db.ServiceOrderHistories.CountAsync(x => x.ActionCode == "ITEM_COMPLETED"));
            Assert.Single(await db.ServiceOrderHistories.Where(x => x.ActionCode == "ORDER_COMPLETED").ToListAsync());
        });
        var changed = new RecordServiceResultsRequest { RequestId = request.RequestId, Items = [request.Items[0]] };
        await Expect(await f.Staff.PostAsJsonAsync(Root + "/" + order.DisplayCode + "/results", changed, Json), HttpStatusCode.Conflict, "IDEMPOTENCY_CONFLICT");
    }

    [PostgreSqlFact]
    public async Task Cancel_cascades_to_exports_is_creator_only_and_does_not_record_duplicate_audits()
    {
        await using var f = await Fixture.CreateAsync();
        var order = await f.CreateOrder();
        var request = new CancelServiceOrderRequest { RequestId = Guid.NewGuid(), Reason = "No longer needed" };
        await Expect(await f.Manager.PostAsJsonAsync(Root + "/" + order.DisplayCode + "/cancel", request, Json), HttpStatusCode.Forbidden);
        await Expect(await f.Staff.PostAsJsonAsync(Root + "/" + order.DisplayCode + "/cancel", request, Json), HttpStatusCode.NoContent);
        await Expect(await f.Staff.PostAsJsonAsync(Root + "/" + order.DisplayCode + "/cancel", request, Json), HttpStatusCode.NoContent);
        var canceled = await f.GetOrder(order.DisplayCode);
        Assert.Equal(ServiceWorkflowStatus.Canceled, canceled.Status);
        Assert.Equal(ServiceWorkflowStatus.Canceled, canceled.ExportSlips[0].Status);
        Assert.All(canceled.Items, i => Assert.Null(i.ExecutionStatus));
        await Expect(await f.Manager.PostAsJsonAsync(Root + "/" + order.DisplayCode + "/approve",
            new ServiceTransitionRequest { RequestId = Guid.NewGuid() }, Json), HttpStatusCode.Conflict);
        var events = await f.Staff.GetFromJsonAsync<JsonElement>(Exports + "/" + order.ExportSlips[0].DisplayCode + "/history");
        Assert.Equal(2, events.GetProperty("totalCount").GetInt32());
    }

    [PostgreSqlFact]
    public async Task Competing_approvals_have_one_winner_and_the_loser_has_no_partial_changes()
    {
        await using var f = await Fixture.CreateAsync();
        var a = await f.CreateOrder();
        var b = await f.CreateOrder();
        var responses = await Task.WhenAll(new[] { a, b }.Select(o => f.Manager.PostAsJsonAsync(Root + "/" + o.DisplayCode + "/approve",
            new ServiceTransitionRequest { RequestId = Guid.NewGuid() }, Json)));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.NoContent);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        await f.InDb(async db =>
        {
            Assert.Equal(3, await db.DeviceOperationHolds.CountAsync());
            Assert.Single(await db.ServiceOrders.Where(x => x.Status == ServiceWorkflowStatus.InProgress).ToListAsync());
            Assert.Single(await db.ServiceOrders.Where(x => x.Status == ServiceWorkflowStatus.PendingApproval).ToListAsync());
            Assert.Equal(3, await db.ServiceOrderItems.CountAsync(x => x.ExecutionStatus == null));
        });
        foreach (var response in responses) response.Dispose();
    }

    [PostgreSqlFact]
    public async Task Export_failure_audit_error_rolls_back_slip_items_parent_and_holds()
    {
        await using var f = await Fixture.CreateAsync();
        var order = await f.CreateOrder();
        await f.Approve(order.DisplayCode);
        f.Host.Failures.FailNextServiceAction = "ITEM_NOT_EXECUTED";
        await Expect(await f.Staff.PostAsJsonAsync(Exports + "/" + order.ExportSlips[0].DisplayCode + "/result",
            new RecordExportResultRequest { RequestId = Guid.NewGuid(), Result = ExportResult.Failed, FailureReason = "Unavailable" }, Json),
            HttpStatusCode.ServiceUnavailable, "DEVICE_WORKFLOW_UNAVAILABLE");
        var current = await f.GetOrder(order.DisplayCode);
        Assert.Equal(ServiceWorkflowStatus.InProgress, current.ExportSlips[0].Status);
        Assert.Null(current.ExportSlips[0].Result);
        Assert.All(current.Items, i => Assert.Equal(ServiceItemStatus.InProgress, i.ExecutionStatus));
        await f.InDb(async db =>
        {
            Assert.Equal(3, await db.DeviceOperationHolds.CountAsync());
            Assert.False(await db.ExportHistories.AnyAsync(x => x.ActionCode == "EXPORT_RESULT_FAILED"));
            Assert.False(await db.ServiceOrderHistories.AnyAsync(x => x.ActionCode == "ITEM_NOT_EXECUTED"));
        });
    }

    [PostgreSqlFact]
    public async Task Public_inputs_reject_manual_not_executed_empty_request_ids_and_department_access()
    {
        await using var f = await Fixture.CreateAsync();
        var order = await f.CreateOrder();
        await f.Approve(order.DisplayCode);
        await Expect(await f.Staff.PostAsJsonAsync(Root + "/" + order.DisplayCode + "/results", new RecordServiceResultsRequest
        {
            RequestId = Guid.NewGuid(), Items = [new() { ItemNo = 1, Result = ServiceItemStatus.NotExecuted, Description = "User selected" }]
        }, Json), HttpStatusCode.BadRequest);
        await Expect(await f.Manager.PostAsJsonAsync(Root + "/" + order.DisplayCode + "/approve", new { requestId = Guid.Empty }), HttpStatusCode.BadRequest);
        await Expect(await f.Staff.PostAsJsonAsync(Exports + "/" + order.ExportSlips[0].DisplayCode + "/result",
            new RecordExportResultRequest { RequestId = Guid.NewGuid(), Result = ExportResult.Success,
                RecipientName = "Recipient", EvidenceRef = "https://example.com/public-document" }, Json), HttpStatusCode.BadRequest);
        using var outsider = f.Host.CreateClient();
        string? token = null;
        await f.InDb(async db =>
        {
            var profile = new UserProfile { FullName = "Department Staff", DepartmentId = f.Department };
            var user = new Account { AccountCode = "TK_DEPT_SERVICE", RoleId = 3, UserProfileId = profile.Id, Email = "dept-service@example.com", PasswordHash = "test-only" };
            token = Guid.NewGuid().ToString("N");
            db.AddRange(profile, user, AccountApiTestHost.Session(user.Id, token));
            await db.SaveChangesAsync();
        });
        outsider.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await Expect(await outsider.GetAsync(Root), HttpStatusCode.Forbidden);
        await Expect(await outsider.GetAsync(Exports), HttpStatusCode.Forbidden);
    }

    [PostgreSqlFact]
    public async Task Database_guards_reject_cross_order_onsite_and_wrong_subtype_writes()
    {
        await using var f = await Fixture.CreateAsync();
        var a = await f.CreateOrder();
        var b = await f.CreateOrder();
        await f.InDb(async db =>
        {
            var slip = await db.ExportSlips.SingleAsync(x => x.DisplayCode == a.ExportSlips[0].DisplayCode);
            var other = await db.ServiceOrders.SingleAsync(x => x.DisplayCode == b.DisplayCode);
            var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE export_slips SET service_order_id={other.Id}, sequence_no=99 WHERE id={slip.Id}"));
            Assert.Equal("ck_export_slip_source_membership", error.ConstraintName);
            var onSite = await db.ServiceOrderItems.SingleAsync(x => x.ServiceOrderId == slip.ServiceOrderId && x.ItemNo == 1);
            var line = await db.ExportSlipItems.FirstAsync(x => x.ExportSlipId == slip.Id);
            error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE export_slip_items SET service_order_item_id={onSite.Id} WHERE id={line.Id}"));
            Assert.Equal("ck_export_slip_source_membership", error.ConstraintName);
            error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO repair_item_details(service_order_item_id) VALUES ({onSite.Id})"));
            Assert.Equal("ck_repair_item_order_type", error.ConstraintName);
            Assert.False(db.Database.HasPendingModelChanges());
        });
    }

    [PostgreSqlFact]
    public async Task Every_failed_external_group_is_terminal_and_the_last_group_completes_parent()
    {
        await using var f = await Fixture.CreateAsync();
        var request = new CreateServiceOrderRequest
        {
            RequestId = Guid.NewGuid(), Items = [
                new() { DeviceCode = "TB_SERVICE_1", ExecutionMethod = ExecutionMethod.External },
                new() { DeviceCode = "TB_SERVICE_2", ExecutionMethod = ExecutionMethod.External }],
            ExportSlips = [new() { FacilityName = "Workshop A", DeviceCodes = ["TB_SERVICE_1"] },
                new() { FacilityName = "Workshop B", DeviceCodes = ["TB_SERVICE_2"] }]
        };
        var order = await f.CreateOrder(request);
        await f.Approve(order.DisplayCode);
        await Expect(await f.Staff.PostAsJsonAsync(Exports + "/" + order.ExportSlips[0].DisplayCode + "/result",
            new RecordExportResultRequest { RequestId = Guid.NewGuid(), Result = ExportResult.Failed, FailureReason = "Declined" }, Json), HttpStatusCode.NoContent);
        var partial = await f.GetOrder(order.DisplayCode);
        Assert.Equal(ServiceItemStatus.NotExecuted, partial.Items[0].ExecutionStatus);
        Assert.Equal(ServiceItemStatus.InProgress, partial.Items[1].ExecutionStatus);
        Assert.Equal(ServiceWorkflowStatus.InProgress, partial.Status);
        await Expect(await f.Staff.PostAsJsonAsync(Exports + "/" + order.ExportSlips[1].DisplayCode + "/result",
            new RecordExportResultRequest { RequestId = Guid.NewGuid(), Result = ExportResult.Failed, FailureReason = "Declined" }, Json), HttpStatusCode.NoContent);
        var finished = await f.GetOrder(order.DisplayCode);
        Assert.Equal(ServiceWorkflowStatus.Completed, finished.Status);
        Assert.All(finished.Items, i => Assert.Equal(ServiceItemStatus.NotExecuted, i.ExecutionStatus));
        await f.InDb(async db => Assert.Empty(await db.DeviceOperationHolds.ToListAsync()));
    }

    [PostgreSqlTheory]
    [InlineData("SERVICE_CREATED")]
    [InlineData("SERVICE_APPROVED")]
    public async Task Audit_failure_during_create_or_approve_rolls_back_all_child_state(string action)
    {
        await using var f = await Fixture.CreateAsync();
        if (action == "SERVICE_CREATED")
        {
            f.Host.Failures.FailNextServiceAction = action;
            await Expect(await f.Staff.PostAsJsonAsync(Root, f.Request(), Json), HttpStatusCode.ServiceUnavailable);
            await f.InDb(async db =>
            {
                Assert.Empty(await db.ServiceOrders.ToListAsync());
                Assert.Empty(await db.ExportSlips.ToListAsync());
                Assert.Empty(await db.ServiceOrderHistories.ToListAsync());
                Assert.Empty(await db.ExportHistories.ToListAsync());
            });
        }
        else
        {
            var order = await f.CreateOrder();
            f.Host.Failures.FailNextServiceAction = action;
            await Expect(await f.Manager.PostAsJsonAsync(Root + "/" + order.DisplayCode + "/approve",
                new ServiceTransitionRequest { RequestId = Guid.NewGuid() }, Json), HttpStatusCode.ServiceUnavailable);
            var current = await f.GetOrder(order.DisplayCode);
            Assert.Equal(ServiceWorkflowStatus.PendingApproval, current.Status);
            Assert.All(current.Items, i => Assert.Null(i.ExecutionStatus));
            Assert.All(current.ExportSlips, e => Assert.Equal(ServiceWorkflowStatus.PendingApproval, e.Status));
        }
        await f.InDb(async db => Assert.Empty(await db.DeviceOperationHolds.ToListAsync()));
    }

    [PostgreSqlFact]
    public async Task Migration_rolls_back_and_reapplies_without_changing_existing_devices()
    {
        await using var f = await Fixture.CreateAsync();
        await f.InDb(async db =>
        {
            var migrator = Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>(db);
            await migrator.MigrateAsync("20261030120000_DeviceParentRelationship");
            Assert.Equal(3, await db.Devices.CountAsync());
            await migrator.MigrateAsync();
            Assert.Empty(await db.ServiceOrders.ToListAsync());
            Assert.False(db.Database.HasPendingModelChanges());
        });
    }

    [PostgreSqlTheory]
    [InlineData(ServiceOrderType.Maintenance, DeviceCondition.Damaged, EntityStatus.Active, false)]
    [InlineData(ServiceOrderType.Repair, DeviceCondition.Damaged, EntityStatus.Active, true)]
    [InlineData(ServiceOrderType.Repair, DeviceCondition.Missing, EntityStatus.Active, false)]
    [InlineData(ServiceOrderType.Repair, DeviceCondition.Good, EntityStatus.Locked, false)]
    public async Task Device_condition_and_lock_are_validated_for_each_service_type(
        ServiceOrderType type, DeviceCondition condition, EntityStatus status, bool allowed)
    {
        await using var f = await Fixture.CreateAsync();
        await f.InDb(async db =>
        {
            var device = await db.Devices.SingleAsync(x => x.DeviceCode == "TB_SERVICE_1");
            device.Condition = condition; device.Status = status;
            await db.SaveChangesAsync();
        });
        var response = await f.Staff.PostAsJsonAsync(Root, f.Request(type), Json);
        await Expect(response, allowed ? HttpStatusCode.Created : HttpStatusCode.Conflict);
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
                    var category = new DeviceCategory { DisplayCode = "CAT_SERVICE", Name = "Service test" };
                    context.Add(category);
                    foreach (var n in Enumerable.Range(1, 3))
                    {
                        context.Add(new DeviceIdentity { DeviceCode = "TB_SERVICE_" + n, Barcode = "BC_SERVICE_" + n, ReservedAt = host.Clock.GetUtcNow() });
                        context.Add(new Device
                    {
                        DeviceCode = "TB_SERVICE_" + n, Barcode = "BC_SERVICE_" + n, Name = "Device " + n,
                        CategoryId = category.Id, DepartmentId = seed.Department, ResponsibleAccountId = seed.Staff
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
            RequestId = Guid.NewGuid(), OrderType = type,
            Items = Enumerable.Range(1, 3).Select(n => new ServiceItemRequest { DeviceCode = "TB_SERVICE_" + n,
                ExecutionMethod = n == 1 ? ExecutionMethod.OnSite : ExecutionMethod.External, WorkDescription = "Check device" }).ToList(),
            ExportSlips = [new() { FacilityName = "External workshop", DeviceCodes = ["TB_SERVICE_2", "TB_SERVICE_3"] }]
        };
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
