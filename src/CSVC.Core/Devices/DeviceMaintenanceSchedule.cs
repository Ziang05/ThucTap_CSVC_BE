namespace CSVC.Core.Devices;

public enum MaintenancePeriodStatus { Scheduled, Due, Completed, Missed }

public sealed class DeviceMaintenanceSchedule
{
    public Guid DeviceId { get; set; }
    public DateOnly StartOn { get; set; }
    public DateOnly? PausedOn { get; set; }
    public Guid CreatedByAccountId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;
}

public sealed class DeviceMaintenancePeriod
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DeviceId { get; set; }
    public DateOnly DueOn { get; set; }
    public DateOnly? ClosesOn { get; set; }
    public int IntervalMonths { get; set; }
    public Guid IntervalSourceCategoryId { get; set; }
    public MaintenancePeriodStatus Status { get; set; }
    public Guid? CompletedByItemId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
