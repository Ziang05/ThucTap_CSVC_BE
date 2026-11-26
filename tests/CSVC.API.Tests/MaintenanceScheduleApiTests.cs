using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CSVC.Core.DeviceCategories;
using CSVC.Core.Devices;
using CSVC.Core.ServiceOrders;
using CSVC.Infrastructure.Persistence;
using CSVC.UseCases.Common;
using CSVC.UseCases.Devices.DTOs;
using CSVC.UseCases.Maintenance;
using CSVC.UseCases.ServiceOrders.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CSVC.API.Tests;

public sealed class MaintenanceScheduleApiTests
{
    private const string DeviceCode = "TB/SCHEDULE";
    private static readonly string DevicePath = "/api/v1/devices/" + Uri.EscapeDataString(DeviceCode);
    private static readonly string Schedule = DevicePath + "/maintenance-schedule";
    private static readonly string Periods = DevicePath + "/maintenance-periods";
    private const string Orders = "/api/v1/service-orders";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [PostgreSqlFact]
    public async Task Due_list_follows_the_current_window_after_the_clock_moves_back()
    {
        await using var fixture = await Fixture.CreateAsync();
        var current = await fixture.ConfigureAsync();
        fixture.Host.Clock.Advance(TimeSpan.FromDays(31));
        await using (var scope = fixture.Host.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<MaintenanceScheduleService>().AdvanceAllAsync(CancellationToken.None);
        var future = await fixture.GetPeriodAsync(MaintenancePeriodStatus.Due);

        fixture.Host.Clock.Advance(TimeSpan.FromDays(-31));
        var due = (await fixture.Staff.GetFromJsonAsync<PagedResult<MaintenancePeriodDto>>(
            "/api/v1/maintenance-periods?status=Due&pageSize=1", Json))!;
        Assert.Equal(1, due.TotalCount);
        var row = Assert.Single(due.Items);
        Assert.Equal(current.Id, row.Id);
        Assert.Equal(MaintenancePeriodStatus.Due, row.Status);
        Assert.True(row.CanCreateOrder);

        var scheduled = (await fixture.Staff.GetFromJsonAsync<PagedResult<MaintenancePeriodDto>>(
            Periods + "?status=Scheduled", Json))!;
        Assert.Contains(scheduled.Items, period => period.Id == future.Id && !period.CanCreateOrder);
        await ExpectAsync(await fixture.Staff.PostAsJsonAsync(Orders, Order(future.Id), Json), HttpStatusCode.Conflict);
        var order = await fixture.ApproveNewOrderAsync(current.Id);
        await fixture.RecordAsync(order, Result(ServiceItemStatus.Completed, current.DueOn), HttpStatusCode.NoContent);
        Assert.Equal(current.Id, (await fixture.GetPeriodAsync(MaintenancePeriodStatus.Completed)).Id);
    }

    [PostgreSqlFact]
    public async Task Board_filter_includes_devices_in_child_categories_and_returns_their_actual_category()
    {
        await using var fixture = await Fixture.CreateAsync();
        var period = await fixture.ConfigureAsync();
        const string list = "/api/v1/maintenance-periods?status=Due&pageSize=1";

        foreach (var filter in new[] { "", "&categoryCode=cat_maint_parent", "&categoryCode=CAT_MAINT_CHILD" })
        {
            var page = (await fixture.Staff.GetFromJsonAsync<PagedResult<MaintenancePeriodDto>>(list + filter, Json))!;
            Assert.Equal(1, page.TotalCount);
            var row = Assert.Single(page.Items);
            Assert.Equal(period.Id, row.Id);
            Assert.Equal(DeviceCode, row.DeviceCode);
            Assert.Equal("CAT_MAINT_CHILD", row.CategoryCode);
            Assert.Equal("Maintenance child", row.CategoryName);
            Assert.Equal("CAT_MAINT_PARENT", row.IntervalSourceCategoryCode);
        }
        var missing = (await fixture.Staff.GetFromJsonAsync<PagedResult<MaintenancePeriodDto>>(
            list + "&categoryCode=UNKNOWN", Json))!;
        Assert.Equal(0, missing.TotalCount);
        Assert.Empty(missing.Items);
    }

    [PostgreSqlFact]
    public async Task Board_search_and_sort_apply_before_paging_within_the_selected_category()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ConfigureAsync();
        const string secondCode = "TB/OTHER";
        await using (var scope = fixture.Host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
            var original = await db.Devices.SingleAsync(device => device.DeviceCode == DeviceCode);
            db.AddRange(new DeviceIdentity { DeviceCode = secondCode, Barcode = "BC_OTHER", ReservedAt = fixture.Host.Clock.GetUtcNow() },
                new Device { DeviceCode = secondCode, Barcode = "BC_OTHER", Name = "Scheduled alpha",
                    CategoryId = original.CategoryId, DepartmentId = original.DepartmentId, Quantity = 1 });
            await db.SaveChangesAsync();
        }
        await ExpectAsync(await fixture.Staff.PutAsJsonAsync("/api/v1/devices/TB%2FOTHER/maintenance-schedule",
            new CreateMaintenanceScheduleRequest { StartOn = new DateOnly(2026, 9, 1) }, Json), HttpStatusCode.OK);

        const string list = "/api/v1/maintenance-periods?status=Due&categoryCode=CAT_MAINT_PARENT&pageSize=1";
        async Task<PagedResult<MaintenancePeriodDto>> Get(string filters)
            => (await fixture.Staff.GetFromJsonAsync<PagedResult<MaintenancePeriodDto>>(list + filters, Json))!;

        var ascending = await Get("&search=%20sChEdUlEd%20&sortBy=Name&sortDirection=Asc");
        Assert.Equal(2, ascending.TotalCount);
        Assert.Equal(secondCode, Assert.Single(ascending.Items).DeviceCode);
        var next = await Get("&search=scheduled&sortBy=Name&sortDirection=Asc&page=2");
        Assert.Equal(2, next.TotalCount);
        Assert.Equal(DeviceCode, Assert.Single(next.Items).DeviceCode);
        Assert.Equal(DeviceCode, Assert.Single((await Get("&sortBy=Name&sortDirection=Desc")).Items).DeviceCode);
        Assert.Equal(DeviceCode, Assert.Single((await Get("&sortBy=DeviceCode&sortDirection=Desc")).Items).DeviceCode);
        Assert.Equal(DeviceCode, Assert.Single((await Get("&sortBy=DueOn&sortDirection=Desc")).Items).DeviceCode);
        var byCode = await Get("&search=OtHeR");
        Assert.Equal(1, byCode.TotalCount);
        Assert.Equal(secondCode, Assert.Single(byCode.Items).DeviceCode);
    }

    [PostgreSqlFact]
    public async Task A_period_keeps_canceled_failed_and_successful_orders_with_one_active_attempt()
    {
        await using var fixture = await Fixture.CreateAsync();
        var period = await fixture.ConfigureAsync();
        var filter = "/api/v1/maintenance-periods?status=Due&departmentCode=PB_TEST&categoryCode=CAT_MAINT_CHILD";
        var due = (await fixture.Staff.GetFromJsonAsync<PagedResult<MaintenancePeriodDto>>(filter, Json))!;
        Assert.Equal(period.Id, Assert.Single(due.Items).Id);

        var requests = new[] { Order(period.Id), Order(period.Id) };
        var responses = await Task.WhenAll(requests.Select(order => fixture.Staff.PostAsJsonAsync(Orders, order, Json)));
        try
        {
            var created = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
            var blocked = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            Assert.Contains("MAINTENANCE_PERIOD_ACTIVE_ORDER", await blocked.Content.ReadAsStringAsync());
            var canceledOrder = (await created.Content.ReadFromJsonAsync<ServiceOrderDto>(Json))!;
            await ExpectAsync(await fixture.Staff.PostAsJsonAsync(Orders + "/" + canceledOrder.DisplayCode + "/cancel",
                new CancelServiceOrderRequest { RequestId = Guid.NewGuid(), Reason = "Reschedule" }, Json), HttpStatusCode.NoContent);
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }

        var failed = await fixture.ApproveNewOrderAsync(period.Id);
        await fixture.RecordAsync(failed, Result(ServiceItemStatus.Failed), HttpStatusCode.NoContent);

        var succeeded = await fixture.ApproveNewOrderAsync(period.Id);
        var success = Result(ServiceItemStatus.Completed);
        await fixture.RecordAsync(succeeded, success, HttpStatusCode.NoContent);
        await fixture.RecordAsync(succeeded, success, HttpStatusCode.NoContent);

        var completed = await fixture.GetPeriodAsync(MaintenancePeriodStatus.Completed);
        Assert.Equal(period.Id, completed.Id);
        Assert.False(completed.CanCreateOrder);
        Assert.Equal(new DateOnly(2026, 10, 21), completed.PerformedOn);
        Assert.Contains(completed.Attempts, attempt => attempt.OrderStatus == ServiceWorkflowStatus.Canceled);
        Assert.Contains(completed.Attempts, attempt => attempt.OrderCode == failed.DisplayCode && attempt.ItemStatus == ServiceItemStatus.Failed);
        var completingAttempt = Assert.Single(completed.Attempts, attempt => attempt.CompletesPeriod);
        Assert.Equal(succeeded.DisplayCode, completingAttempt.OrderCode);

        var reopened = await fixture.Staff.GetFromJsonAsync<ServiceOrderDto>(Orders + "/" + completingAttempt.OrderCode, Json);
        var line = Assert.Single(reopened!.Items);
        Assert.Equal(period.Id, line.MaintenancePeriodId);
        Assert.Equal(period.DueOn, line.MaintenanceDueOn);
    }

    [PostgreSqlFact]
    public async Task Manual_orders_attach_the_open_period_and_record_Vietnam_date_on_success()
    {
        await using var fixture = await Fixture.CreateAsync();
        var period = await fixture.ConfigureAsync();
        const string picker = "/api/v1/devices?search=TB%2FSCHEDULE&includeMaintenanceDue=true";
        var devices = (await fixture.Staff.GetFromJsonAsync<PagedResult<DeviceDto>>(picker, Json))!;
        Assert.Equal(period.DueOn, Assert.Single(devices.Items).MaintenanceDueOn);

        var manual = Order(null);
        var order = await fixture.CreateOrderAsync(manual);
        Assert.Equal(period.Id, Assert.Single(order.Items).MaintenancePeriodId);
        Assert.Equal(period.DueOn, Assert.Single(order.Items).MaintenanceDueOn);
        Assert.Equal(order.DisplayCode, (await fixture.CreateOrderAsync(manual)).DisplayCode);
        using (var duplicate = await fixture.Staff.PostAsJsonAsync(Orders, Order(null), Json))
        {
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            Assert.Contains("MAINTENANCE_PERIOD_ACTIVE_ORDER", await duplicate.Content.ReadAsStringAsync());
        }
        await fixture.ApproveAsync(order);
        // 18:00 UTC is already the next calendar day in Vietnam.
        fixture.Host.Clock.Advance(TimeSpan.FromHours(6));
        var success = Result(ServiceItemStatus.Completed);
        await fixture.RecordAsync(order, success, HttpStatusCode.NoContent);
        fixture.Host.Clock.Advance(TimeSpan.FromDays(1));
        await fixture.RecordAsync(order, success, HttpStatusCode.NoContent);

        var completed = await fixture.GetPeriodAsync(MaintenancePeriodStatus.Completed);
        Assert.Equal(period.Id, completed.Id);
        Assert.Equal(new DateOnly(2026, 10, 22), completed.PerformedOn);
        Assert.True(Assert.Single(completed.Attempts).CompletesPeriod);
        devices = (await fixture.Staff.GetFromJsonAsync<PagedResult<DeviceDto>>(picker, Json))!;
        Assert.Null(Assert.Single(devices.Items).MaintenanceDueOn);
        var outside = await fixture.CreateOrderAsync(Order(null));
        Assert.Null(Assert.Single(outside.Items).MaintenancePeriodId);
    }

    [PostgreSqlFact]
    public async Task Success_after_closure_records_today_without_completing_the_expired_or_next_period()
    {
        await using var fixture = await Fixture.CreateAsync();
        var period = await fixture.ConfigureAsync();
        var order = await fixture.ApproveNewOrderAsync(period.Id);
        fixture.Host.Clock.Advance(TimeSpan.FromDays(31));
        await using (var scope = fixture.Host.Services.CreateAsyncScope())
        {
            var worker = scope.ServiceProvider.GetRequiredService<MaintenanceScheduleService>();
            await worker.AdvanceAllAsync(CancellationToken.None);
        }

        var missed = await fixture.GetPeriodAsync(MaintenancePeriodStatus.Missed);
        Assert.Equal(period.Id, missed.Id);
        var next = await fixture.GetPeriodAsync(MaintenancePeriodStatus.Due);
        Assert.Equal(new DateOnly(2026, 11, 21), next.DueOn);

        await fixture.RecordAsync(order, Result(ServiceItemStatus.Completed, new DateOnly(2026, 11, 20)), HttpStatusCode.NoContent);
        var old = await fixture.GetPeriodAsync(MaintenancePeriodStatus.Missed);
        var attempt = Assert.Single(old.Attempts);
        Assert.Equal(new DateOnly(2026, 11, 21), attempt.PerformedOn);
        Assert.False(attempt.CompletesPeriod);
        Assert.Equal(next.Id, (await fixture.GetPeriodAsync(MaintenancePeriodStatus.Due)).Id);
    }

    private static CreateServiceOrderRequest Order(Guid? periodId) => new()
    {
        RequestId = Guid.NewGuid(),
        Items = [new() { DeviceCode = DeviceCode, ExecutionMethod = ExecutionMethod.OnSite, MaintenancePeriodId = periodId }]
    };

    private static RecordServiceResultsRequest Result(ServiceItemStatus result, DateOnly? performedOn = null) => new()
    {
        RequestId = Guid.NewGuid(),
        Items = [new() { ItemNo = 1, Result = result, Description = "Inspection result", PerformedOn = performedOn }]
    };

    private static async Task ExpectAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        using (response)
            Assert.True(response.StatusCode == status, $"Expected {status}, received {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    private sealed class Fixture(AccountTestDatabase database, AccountApiTestHost host, HttpClient staff, HttpClient manager)
        : IAsyncDisposable
    {
        public AccountApiTestHost Host => host;
        public HttpClient Staff => staff;

        public static async Task<Fixture> CreateAsync()
        {
            var database = await AccountTestDatabase.CreateAsync();
            var host = new AccountApiTestHost(database.ConnectionString);
            try
            {
                var seed = await host.SeedAsync();
                await using var scope = host.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
                var settings = await db.EquipmentSettings.SingleAsync();
                settings.EquipmentDepartmentId = seed.Department;
                var parent = new DeviceCategory { DisplayCode = "CAT_MAINT_PARENT", Name = "Maintenance parent", MaintenanceIntervalMonths = 1 };
                var child = new DeviceCategory { DisplayCode = "CAT_MAINT_CHILD", Name = "Maintenance child", ParentId = parent.Id };
                db.AddRange(parent, child,
                    new DeviceIdentity { DeviceCode = DeviceCode, Barcode = "BC_SCHEDULE", ReservedAt = host.Clock.GetUtcNow() },
                    new Device { DeviceCode = DeviceCode, Barcode = "BC_SCHEDULE", Name = "Scheduled device",
                        CategoryId = child.Id, DepartmentId = seed.Department, Quantity = 1 });
                foreach (var session in await db.AuthSessions.ToListAsync())
                    session.ExpiresAt = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
                await db.SaveChangesAsync();
                var staff = host.CreateClient();
                var manager = host.CreateClient();
                staff.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", seed.StaffToken);
                manager.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", seed.ManagerToken);
                return new(database, host, staff, manager);
            }
            catch
            {
                await host.DisposeAsync();
                await database.DisposeAsync();
                throw;
            }
        }

        public async Task<MaintenancePeriodDto> ConfigureAsync()
        {
            await ExpectAsync(await staff.PutAsJsonAsync(Schedule, new CreateMaintenanceScheduleRequest { StartOn = new DateOnly(2026, 9, 21) }, Json), HttpStatusCode.OK);
            var period = await GetPeriodAsync(MaintenancePeriodStatus.Due);
            Assert.Equal(new DateOnly(2026, 10, 21), period.DueOn);
            Assert.Equal(new DateOnly(2026, 11, 21), period.ClosesOn);
            Assert.Equal("CAT_MAINT_PARENT", period.IntervalSourceCategoryCode);
            Assert.True(period.CanCreateOrder);
            return period;
        }

        public async Task<MaintenancePeriodDto> GetPeriodAsync(MaintenancePeriodStatus status)
        {
            var result = await staff.GetFromJsonAsync<PagedResult<MaintenancePeriodDto>>(Periods + "?status=" + status, Json);
            return Assert.Single(result!.Items);
        }

        public async Task<ServiceOrderDto> ApproveNewOrderAsync(Guid periodId)
        {
            var order = await CreateOrderAsync(Order(periodId));
            await ApproveAsync(order);
            return order;
        }

        public async Task<ServiceOrderDto> CreateOrderAsync(CreateServiceOrderRequest request)
        {
            using var response = await staff.PostAsJsonAsync(Orders, request, Json);
            Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<ServiceOrderDto>(Json))!;
        }

        public async Task ApproveAsync(ServiceOrderDto order)
            => await ExpectAsync(await manager.PostAsJsonAsync(Orders + "/" + order.DisplayCode + "/approve",
                new ServiceTransitionRequest { RequestId = Guid.NewGuid(), Version = order.Version }, Json), HttpStatusCode.NoContent);

        public async Task RecordAsync(ServiceOrderDto order, RecordServiceResultsRequest request, HttpStatusCode expected)
            => await ExpectAsync(await staff.PostAsJsonAsync(Orders + "/" + order.DisplayCode + "/results", request, Json), expected);

        public async ValueTask DisposeAsync()
        {
            staff.Dispose();
            manager.Dispose();
            await host.DisposeAsync();
            await database.DisposeAsync();
        }
    }
}
