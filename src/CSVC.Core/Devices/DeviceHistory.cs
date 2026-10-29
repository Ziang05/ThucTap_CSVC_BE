using CSVC.Core.Common;

namespace CSVC.Core.Devices;

public sealed class DeviceHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequestId { get; set; }
    public Guid? ActorAccountId { get; set; }
    public required string ActionCode { get; set; }
    public AuditOutcome Outcome { get; set; }
    public string? ReasonCode { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public Guid? DeviceId { get; set; }
    public string? ChangedFields { get; set; }
    public string? BeforeData { get; set; }
    public string? AfterData { get; set; }
}
