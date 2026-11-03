using CSVC.Core.Common;

namespace CSVC.Core.ServiceOrders;

public sealed class ServiceOrderHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequestId { get; set; }
    public Guid ServiceOrderId { get; set; }
    public Guid? ServiceOrderItemId { get; set; }
    public Guid? RelatedExportSlipId { get; set; }
    public required string TargetKey { get; set; }
    public Guid? ActorAccountId { get; set; }
    public required string ActionCode { get; set; }
    public AuditOutcome Outcome { get; set; }
    public string? ReasonCode { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string? BeforeData { get; set; }
    public string? AfterData { get; set; }
}

public sealed class ExportHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequestId { get; set; }
    public Guid ExportSlipId { get; set; }
    public Guid? ActorAccountId { get; set; }
    public required string ActionCode { get; set; }
    public AuditOutcome Outcome { get; set; }
    public string? ReasonCode { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string? BeforeData { get; set; }
    public string? AfterData { get; set; }
}
