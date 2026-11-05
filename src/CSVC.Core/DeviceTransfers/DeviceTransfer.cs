using CSVC.Core.Devices;

namespace CSVC.Core.DeviceTransfers;

public enum DeviceTransferStatus
{
    PendingApproval,
    Rejected,
    Approved,
    Completed,
    Cancelled,
    PendingConfirmation
}

public enum DeviceTransferType { Allocation, InterDepartment, Recovery }

public enum TransferInitiationType { SourceDepartment, EquipmentDepartment }

public sealed class DeviceTransfer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string TransferCode { get; set; }
    public Guid SourceDepartmentId { get; set; }
    public Guid TargetDepartmentId { get; set; }
    public Guid TargetResponsibleAccountId { get; set; }
    public required string Reason { get; set; }
    public Guid CreatorAccountId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DeviceTransferStatus Status { get; set; } = DeviceTransferStatus.PendingConfirmation;
    public DeviceTransferType TransferType { get; set; }
    public TransferInitiationType InitiationType { get; set; } = TransferInitiationType.SourceDepartment;
    public Guid? CreateRequestId { get; set; }
    public string? CreateRequestHash { get; set; }
    public Guid? SourceAcceptedByAccountId { get; set; }
    public DateTimeOffset? SourceAcceptedAt { get; set; }
    public Guid? SourceRejectedByAccountId { get; set; }
    public DateTimeOffset? SourceRejectedAt { get; set; }
    public string? SourceRejectionReason { get; set; }
    public Guid? SourceHandoverConfirmedByAccountId { get; set; }
    public DateTimeOffset? SourceHandoverConfirmedAt { get; set; }
    public Guid? ReceiverConfirmedAccountId { get; set; }
    public DateTimeOffset? ReceiverConfirmedAt { get; set; }
    public Guid? ReceiverRejectedAccountId { get; set; }
    public DateTimeOffset? ReceiverRejectedAt { get; set; }
    public string? ReceiverRejectionReason { get; set; }
    public Guid? DecisionAccountId { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public Guid? SourceHandoverAccountId { get; set; }
    public Guid? TargetHandoverAccountId { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? DecisionReason { get; set; }
    public string? Notes { get; set; }
    public int Version { get; set; } = 1;
    public ICollection<DeviceTransferItem> Items { get; set; } = new List<DeviceTransferItem>();
}

public sealed class DeviceTransferItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TransferId { get; set; }
    public Guid DeviceId { get; set; }
    public DeviceCondition? ConditionAtHandover { get; set; }
    public string? HandoverNote { get; set; }
    public DeviceCondition? ConditionAtSourceHandover { get; set; }
    public string? SourceHandoverNote { get; set; }
}
