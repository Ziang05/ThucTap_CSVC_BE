using CSVC.Core.Common;
using CSVC.Core.Devices;

namespace CSVC.Core.ServiceOrders;

public sealed class ServiceOrderDeviceHold
{
    public Guid DeviceId { get; set; }
    public Guid ServiceOrderId { get; set; }
    public Guid ServiceOrderItemId { get; set; }
    public Guid HeldByAccountId { get; set; }
    public DateTimeOffset HeldAt { get; set; }
    public int Version { get; set; } = 1;
}

public enum ExportGuardState { HeldForExport, AwaitingReturn }
public sealed class ExportDeviceGuard
{
    public Guid DeviceId { get; set; }
    public Guid ExportSlipItemId { get; set; }
    public ExportGuardState GuardState { get; set; }
    public DateTimeOffset AcquiredAt { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
    public int Version { get; set; } = 1;
}

public sealed class ExportReturnSlip
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ExportSlipId { get; set; }
    public int SequenceNo { get; set; }
    public required string DisplayCode { get; set; }
    public string Status { get; set; } = "Completed";
    public required string FacilityNameSnapshot { get; set; }
    public required string HandoverPersonName { get; set; }
    public string? HandoverPersonContact { get; set; }
    public Guid ReceivedByAccountId { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public Guid CreatedByAccountId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? Notes { get; set; }
    public Guid CreateRequestId { get; set; }
    public required string CreateRequestHash { get; set; }
    public int Version { get; set; } = 1;
    public ICollection<ExportReturnSlipItem> Items { get; set; } = new List<ExportReturnSlipItem>();
}

public sealed class ExportReturnSlipItem
{
    public ExportReturnSlip ReturnSlip { get; set; } = null!;
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ReturnSlipId { get; set; }
    public Guid ExportSlipId { get; set; }
    public Guid ExportSlipItemId { get; set; }
    public required string DeviceCodeSnapshot { get; set; }
    public required string DeviceNameSnapshot { get; set; }
    public string? SerialSnapshot { get; set; }
    public DeviceCondition ConditionAtReturn { get; set; }
    public string? ReturnNote { get; set; }
}

public sealed class ExportReturnHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ReturnSlipId { get; set; }
    public Guid? ReturnSlipItemId { get; set; }
    public Guid RequestId { get; set; }
    public string? RequestPayloadHash { get; set; }
    public required string TargetKey { get; set; }
    public Guid ActorAccountId { get; set; }
    public required string ActionCode { get; set; }
    public AuditOutcome Outcome { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string? BeforeData { get; set; }
    public string? AfterData { get; set; }
}
