using CSVC.Core.Common;
using CSVC.Core.Devices;
using CSVC.Core.ServiceOrders;
using CSVC.UseCases.Common;
using CSVC.UseCases.Maintenance;
using CSVC.UseCases.ServiceOrders.DTOs;
using Microsoft.EntityFrameworkCore;

namespace CSVC.Infrastructure.Persistence.Repositories;

public sealed class MaintenanceScheduleRepository(CsvcDbContext context) : IMaintenanceScheduleRepository
{
    private IQueryable<DeviceMaintenancePeriod> Periods => context.Set<DeviceMaintenancePeriod>();
    private IQueryable<DeviceMaintenanceSchedule> Schedules => context.Set<DeviceMaintenanceSchedule>();

    public Task<DateOnly?> FirstDueOnAsync(Guid deviceId, CancellationToken ct)
        => Periods.Where(period => period.DeviceId == deviceId).OrderBy(period => period.DueOn)
            .Select(period => (DateOnly?)period.DueOn).FirstOrDefaultAsync(ct);

    public Task<DeviceMaintenancePeriod?> LatestTrackedAsync(Guid deviceId, CancellationToken ct)
        => Periods.Where(period => period.DeviceId == deviceId)
            .OrderByDescending(period => period.DueOn).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<Guid>> DevicesToAdvanceAsync(DateOnly today, CancellationToken ct)
        => await Schedules.Where(schedule => !Periods.Any(period => period.DeviceId == schedule.DeviceId
            && period.Status == MaintenancePeriodStatus.Scheduled && period.DueOn > today))
            .OrderBy(schedule => schedule.DeviceId).Select(schedule => schedule.DeviceId).ToListAsync(ct);

    private IQueryable<DeviceMaintenancePeriod> OpenPeriods(DateOnly today)
        => Periods.Where(period => (period.Status == MaintenancePeriodStatus.Due || period.Status == MaintenancePeriodStatus.Missed)
            && period.DueOn <= today && period.ClosesOn > today);

    public Task<DeviceMaintenancePeriod?> FindOpenPeriodAsync(Guid deviceId, DateOnly today, CancellationToken ct)
        => OpenPeriods(today).Where(period => period.DeviceId == deviceId)
            .OrderByDescending(period => period.DueOn).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, DateOnly>> ListOpenDueDatesAsync(Guid[] deviceIds, DateOnly today, CancellationToken ct)
        => await OpenPeriods(today).Where(period => deviceIds.Contains(period.DeviceId))
            .GroupBy(period => period.DeviceId)
            .Select(group => new { DeviceId = group.Key, DueOn = group.Max(period => period.DueOn) })
            .ToDictionaryAsync(period => period.DeviceId, period => period.DueOn, ct);

    public Task<bool> HasActiveAttemptAsync(Guid periodId, CancellationToken ct)
        => (from item in context.ServiceOrderItems
            join order in context.ServiceOrders on item.ServiceOrderId equals order.Id
            where item.MaintenancePeriodId == periodId && order.OrderType == ServiceOrderType.Maintenance
                && (order.Status == ServiceWorkflowStatus.PendingApproval && item.ExecutionStatus == null
                    || order.Status == ServiceWorkflowStatus.InProgress && item.ExecutionStatus == ServiceItemStatus.InProgress)
            select item.Id).AnyAsync(ct);

    public async Task<PagedResult<MaintenancePeriodDto>> ListAsync(MaintenancePeriodQuery query, Guid? deviceId,
        Guid? departmentId, bool includeAttempts, DateOnly today, CancellationToken ct)
    {
        var deviceCode = FilterCode(query.DeviceCode);
        var departmentCode = FilterCode(query.DepartmentCode);
        var source = from period in Periods.AsNoTracking()
                     join device in context.Devices on period.DeviceId equals device.Id
                     join department in context.Departments on device.DepartmentId equals department.Id
                     join category in context.DeviceCategories on device.CategoryId equals category.Id
                     join intervalSource in context.DeviceCategories on period.IntervalSourceCategoryId equals intervalSource.Id
                     // Re-evaluate opened periods against today's window, even if the clock moved since the last worker run.
                     let status = period.ClosesOn == null || period.Status == MaintenancePeriodStatus.Completed
                         ? period.Status
                         : period.DueOn > today ? MaintenancePeriodStatus.Scheduled
                         : period.ClosesOn <= today ? MaintenancePeriodStatus.Missed
                         : MaintenancePeriodStatus.Due
                     where (deviceId == null || device.Id == deviceId) && (departmentId == null || department.Id == departmentId)
                         && (deviceCode == null || device.DeviceCode == deviceCode)
                         && (departmentCode == null || department.Code == departmentCode)
                         && (query.Status == null || status == query.Status)
                         && (query.DueFrom == null || period.DueOn >= query.DueFrom)
                         && (query.DueTo == null || period.DueOn <= query.DueTo)
                     select new
                     {
                         Period = period, Status = status, device.DeviceCode, DeviceName = device.Name, device.SerialNumber,
                         DepartmentCode = department.Code, DepartmentName = department.Name,
                         device.CategoryId, CategoryCode = category.DisplayCode, CategoryName = category.Name,
                         IntervalSource = intervalSource.DisplayCode,
                         Eligible = device.Quantity == 1 && device.Condition == DeviceCondition.Good,
                         Busy = includeAttempts && (context.DeviceOperationHolds.Any(hold => hold.DeviceId == device.Id)
                             || context.Set<ServiceOrderDeviceHold>().Any(hold => hold.DeviceId == device.Id)
                             || context.Set<ExportDeviceGuard>().Any(guard => guard.DeviceId == device.Id)),
                         PerformedOn = context.ServiceOrderItems.Where(item => item.Id == period.CompletedByItemId)
                             .Select(item => item.PerformedOn).FirstOrDefault()
                     };
        if (!string.IsNullOrWhiteSpace(query.CategoryCode))
        {
            var branch = DeviceCategoryRepository.SubtreeIds(context, query.CategoryCode.Trim().ToLowerInvariant());
            source = source.Where(row => branch.Contains(row.CategoryId));
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = TextNormalization.Name(query.Search).ToLowerInvariant();
            source = source.Where(row => row.DeviceName.ToLower().Contains(search) || row.DeviceCode.ToLower().Contains(search));
        }
        var total = await source.CountAsync(ct);
        var direction = query.SortDirection ?? (query.SortBy == null && deviceId.HasValue
            ? MaintenancePeriodSortDirection.Desc : MaintenancePeriodSortDirection.Asc);
        var sorted = (query.SortBy, direction) switch
        {
            (MaintenancePeriodSortBy.Name, MaintenancePeriodSortDirection.Desc) => source.OrderByDescending(row => row.DeviceName),
            (MaintenancePeriodSortBy.Name, _) => source.OrderBy(row => row.DeviceName),
            (MaintenancePeriodSortBy.DeviceCode, MaintenancePeriodSortDirection.Desc) => source.OrderByDescending(row => row.DeviceCode),
            (MaintenancePeriodSortBy.DeviceCode, _) => source.OrderBy(row => row.DeviceCode),
            (_, MaintenancePeriodSortDirection.Desc) => source.OrderByDescending(row => row.Period.DueOn),
            _ => source.OrderBy(row => row.Period.DueOn)
        };
        var page = await sorted.ThenBy(row => row.DeviceCode).ThenBy(row => row.Period.DueOn).ThenBy(row => row.Period.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        var periodIds = page.Select(row => row.Period.Id).ToArray();
        var attempts = includeAttempts && periodIds.Length > 0
            ? await AttemptRows().Where(row => row.PeriodId.HasValue && periodIds.Contains(row.PeriodId.Value))
                .OrderByDescending(row => row.CreatedAt).ThenBy(row => row.OrderCode).ToListAsync(ct)
            : [];
        var attemptsByPeriod = attempts.ToLookup(row => row.PeriodId);
        var items = page.Select(row =>
        {
            var period = row.Period;
            var related = attemptsByPeriod[period.Id].ToArray();
            var canManage = includeAttempts && row.Status == MaintenancePeriodStatus.Due;
            MaintenanceOrderBlock? block = null;
            if (canManage)
            {
                if (related.Any(IsActive)) block = MaintenanceOrderBlock.ActiveOrder;
                else if (row.Busy) block = MaintenanceOrderBlock.DeviceBusy;
                else if (!row.Eligible) block = MaintenanceOrderBlock.DeviceUnavailable;
            }

            return new MaintenancePeriodDto(period.Id, row.DeviceCode, row.DeviceName, row.SerialNumber, row.DepartmentCode,
                row.DepartmentName, row.CategoryCode, period.DueOn, period.ClosesOn, period.IntervalMonths, row.IntervalSource,
                row.Status, canManage && block is null, row.PerformedOn,
                related.Select(attempt => ToAttempt(attempt, period.CompletedByItemId)).ToArray())
                { OrderBlock = block, CategoryName = row.CategoryName };
        }).ToArray();
        return new(items, total, query.Page, query.PageSize);
    }

    public async Task<PagedResult<MaintenanceAttemptDto>> ListUnscheduledAsync(Guid deviceId, ServiceHistoryQuery query, CancellationToken ct)
    {
        var source = AttemptRows().Where(row => row.DeviceId == deviceId && row.PeriodId == null);
        var total = await source.CountAsync(ct);
        var page = await source.OrderByDescending(row => row.CreatedAt).ThenBy(row => row.OrderCode)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new(page.Select(row => ToAttempt(row, null)).ToArray(), total, query.Page, query.PageSize);
    }

    private IQueryable<AttemptRow> AttemptRows()
        => from item in context.ServiceOrderItems
           join order in context.ServiceOrders on item.ServiceOrderId equals order.Id
           where order.OrderType == ServiceOrderType.Maintenance
           select new AttemptRow
           {
               Id = item.Id, PeriodId = item.MaintenancePeriodId, DeviceId = item.DeviceId,
               OrderId = order.Id, OrderCode = order.DisplayCode, ItemNo = item.ItemNo, OrderStatus = order.Status,
               ItemStatus = item.ExecutionStatus, ResultDescription = item.ResultDescription,
               PerformedOn = item.PerformedOn, CreatedAt = order.CreatedAt
           };

    private static bool IsActive(AttemptRow row)
        => row is { OrderStatus: ServiceWorkflowStatus.PendingApproval, ItemStatus: null }
            or { OrderStatus: ServiceWorkflowStatus.InProgress, ItemStatus: ServiceItemStatus.InProgress };

    private static MaintenanceAttemptDto ToAttempt(AttemptRow row, Guid? completedBy)
        => new(row.OrderId, row.OrderCode, row.ItemNo, row.OrderStatus, row.ItemStatus, row.ResultDescription,
            row.PerformedOn, row.CreatedAt, row.Id == completedBy);

    private static string? FilterCode(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private sealed class AttemptRow
    {
        public Guid Id { get; init; }
        public Guid? PeriodId { get; init; }
        public Guid DeviceId { get; init; }
        public Guid OrderId { get; init; }
        public required string OrderCode { get; init; }
        public int ItemNo { get; init; }
        public ServiceWorkflowStatus OrderStatus { get; init; }
        public ServiceItemStatus? ItemStatus { get; init; }
        public string? ResultDescription { get; init; }
        public DateOnly? PerformedOn { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
    }
}
