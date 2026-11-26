using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using CSVC.Core.Devices;
using CSVC.Core.ServiceOrders;
using CSVC.UseCases.Common;
using CSVC.UseCases.ServiceOrders.DTOs;

namespace CSVC.UseCases.Maintenance;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateMaintenanceScheduleRequest
{
    [Required] public DateOnly? StartOn { get; init; }
}

public sealed record MaintenanceScheduleDto(DateOnly? StartOn, DateOnly? PausedOn, int? IntervalMonths,
    string? IntervalSourceCategoryCode, DateOnly? FirstTrackedDueOn);

public sealed class MaintenancePeriodQuery
{
    [Range(1, 1_000_000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
    [EnumDataType(typeof(MaintenancePeriodStatus))] public MaintenancePeriodStatus? Status { get; init; }
    public DateOnly? DueFrom { get; init; }
    public DateOnly? DueTo { get; init; }
    [StringLength(200)] public string? Search { get; init; }
    [EnumDataType(typeof(MaintenancePeriodSortBy))] public MaintenancePeriodSortBy? SortBy { get; init; }
    [EnumDataType(typeof(MaintenancePeriodSortDirection))] public MaintenancePeriodSortDirection? SortDirection { get; init; }
    [StringLength(32)] public string? DeviceCode { get; init; }
    [StringLength(64)] public string? DepartmentCode { get; init; }
    [StringLength(64)] public string? CategoryCode { get; init; }
}

public enum MaintenancePeriodSortBy { DueOn, Name, DeviceCode }
public enum MaintenancePeriodSortDirection { Asc, Desc }

public sealed record MaintenancePeriodDto(Guid Id, string DeviceCode, string DeviceName, string? SerialNumber,
    string DepartmentCode, string DepartmentName, string CategoryCode, DateOnly DueOn, DateOnly? ClosesOn,
    int IntervalMonths, string IntervalSourceCategoryCode, MaintenancePeriodStatus Status, bool CanCreateOrder,
    DateOnly? PerformedOn, IReadOnlyList<MaintenanceAttemptDto> Attempts)
{
    public string? CategoryName { get; init; }
    public MaintenanceOrderBlock? OrderBlock { get; init; }
}

public enum MaintenanceOrderBlock { ActiveOrder, DeviceBusy, DeviceUnavailable }

public sealed record MaintenanceAttemptDto(Guid OrderId, string OrderCode, int ItemNo, ServiceWorkflowStatus OrderStatus,
    ServiceItemStatus? ItemStatus, string? ResultDescription, DateOnly? PerformedOn, DateTimeOffset CreatedAt,
    bool CompletesPeriod);

public interface IMaintenanceScheduleRepository
{
    Task<DateOnly?> FirstDueOnAsync(Guid deviceId, CancellationToken ct);
    Task<DeviceMaintenancePeriod?> LatestTrackedAsync(Guid deviceId, CancellationToken ct);
    Task<IReadOnlyList<Guid>> DevicesToAdvanceAsync(DateOnly today, CancellationToken ct);
    Task<DeviceMaintenancePeriod?> FindOpenPeriodAsync(Guid deviceId, DateOnly today, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, DateOnly>> ListOpenDueDatesAsync(Guid[] deviceIds, DateOnly today, CancellationToken ct);
    Task<bool> HasActiveAttemptAsync(Guid periodId, CancellationToken ct);
    Task<PagedResult<MaintenancePeriodDto>> ListAsync(MaintenancePeriodQuery query, Guid? deviceId,
        Guid? departmentId, bool includeAttempts, DateOnly today, CancellationToken ct);
    Task<PagedResult<MaintenanceAttemptDto>> ListUnscheduledAsync(Guid deviceId, ServiceHistoryQuery query, CancellationToken ct);
}
