namespace CSVC.Core.ServiceOrders;

public enum ServiceOrderType { Maintenance, Repair }
public enum ServiceWorkflowStatus { PendingApproval, InProgress, Completed, Canceled }
public enum ExecutionMethod { OnSite, External }
public enum ServiceItemStatus { InProgress, NotExecuted, Completed, Failed }
public enum ExportResult { Success, Failed }

public sealed class ServiceOrder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public ServiceOrderType OrderType { get; set; }
    public required string DisplayCode { get; set; }
    public Guid CreateRequestId { get; set; }
    public required string CreateRequestHash { get; set; }
    public ServiceWorkflowStatus Status { get; set; }
    public Guid CreatedByAccountId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? ApprovedByAccountId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public Guid? ApprovalRequestId { get; set; }
    public Guid? CanceledByAccountId { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public Guid? CancelRequestId { get; set; }
    public string? CancelReason { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? Notes { get; set; }
    public int Version { get; set; }
    public ICollection<ServiceOrderItem> Items { get; set; } = new List<ServiceOrderItem>();
    public ICollection<ExportSlip> ExportSlips { get; set; } = new List<ExportSlip>();
}

public sealed class ServiceOrderItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ServiceOrderId { get; set; }
    public int ItemNo { get; set; }
    public Guid DeviceId { get; set; }
    public ExecutionMethod ExecutionMethod { get; set; }
    public ServiceItemStatus? ExecutionStatus { get; set; }
    public string? WorkDescription { get; set; }
    public string? ResultDescription { get; set; }
    public Guid? ResultRecordedByAccountId { get; set; }
    public DateTimeOffset? ResultRecordedAt { get; set; }
    public Guid? ResultRequestId { get; set; }
    public string? Notes { get; set; }
    public required string DeviceCodeSnapshot { get; set; }
    public required string DeviceNameSnapshot { get; set; }
    public string? SerialSnapshot { get; set; }
    public int Version { get; set; }
    public RepairItemDetail? RepairDetail { get; set; }
}

public sealed class RepairItemDetail
{
    public Guid ServiceOrderItemId { get; set; }
    // External dependency: no damage-report master exists in this repository yet.
    public Guid? DamageReportId { get; set; }
}

public sealed class ExportSlip
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ServiceOrderId { get; set; }
    public int SequenceNo { get; set; }
    public required string DisplayCode { get; set; }
    public ServiceWorkflowStatus Status { get; set; }
    // External dependency; creation currently uses the facility snapshots below.
    public Guid? FacilityId { get; set; }
    public required string FacilityNameSnapshot { get; set; }
    public string? FacilityAddressSnapshot { get; set; }
    public string? FacilityContactSnapshot { get; set; }
    public DateTimeOffset? PlannedExportAt { get; set; }
    public string? Notes { get; set; }
    public Guid CreatedByAccountId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public ExportResult? Result { get; set; }
    public Guid? ResultRecordedByAccountId { get; set; }
    public DateTimeOffset? ResultRecordedAt { get; set; }
    public Guid? ResultRequestId { get; set; }
    public string? RecipientName { get; set; }
    public string? RecipientContact { get; set; }
    public string? FailureReason { get; set; }
    public string? EvidenceRef { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int Version { get; set; }
    public ICollection<ExportSlipItem> Items { get; set; } = new List<ExportSlipItem>();
}

public sealed class ExportSlipItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ExportSlipId { get; set; }
    public Guid ServiceOrderItemId { get; set; }
    public required string DeviceCodeSnapshot { get; set; }
    public required string DeviceNameSnapshot { get; set; }
    public string? SerialSnapshot { get; set; }
}
