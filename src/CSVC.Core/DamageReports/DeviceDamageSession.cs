namespace CSVC.Core.DamageReports;

public enum DamageVerificationStatus { Pending, ConfirmedDamaged, ConfirmedNotDamaged }

public sealed class DeviceDamageSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DeviceId { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    public Guid OpenedByAccountId { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public Guid? ClosedByAccountId { get; set; }
    public int Version { get; set; } = 1;
}

public sealed class DamageReportDecision
{
    public Guid DamageReportId { get; set; }
    public Guid DeviceId { get; set; }
    public Guid? DamageSessionId { get; set; }
    public DamageVerificationStatus Outcome { get; set; }
    public required string Reason { get; set; }
    public DateTimeOffset DecidedAt { get; set; }
    public Guid DecidedByAccountId { get; set; }
}

public sealed class DamageSessionInspection
{
    public Guid RequestId { get; set; }
    public Guid DamageSessionId { get; set; }
    public Guid ServiceOrderItemId { get; set; }
    public int ExpectedVersion { get; set; }
    public bool Passed { get; set; }
    public required string Notes { get; set; }
    public DateTimeOffset InspectedAt { get; set; }
    public Guid InspectedByAccountId { get; set; }
}
