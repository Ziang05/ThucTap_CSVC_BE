using CSVC.Core.Common;
using CSVC.Core.DeviceTransfers;
using CSVC.Core.Devices;

namespace CSVC.UseCases.DeviceTransfers.Models;

public sealed class DeviceTransferReadModel
{
    public Guid Id { get; init; }
    public required string TransferCode { get; init; }
    public required string SourceDepartmentCode { get; init; }
    public required string SourceDepartmentName { get; init; }
    public required string TargetDepartmentCode { get; init; }
    public required string TargetDepartmentName { get; init; }
    public required string TargetResponsibleAccountCode { get; init; }
    public required string TargetResponsibleFullName { get; init; }
    public required string Reason { get; init; }
    public DeviceTransferStatus Status { get; init; }
    public DeviceTransferType TransferType { get; init; }
    public TransferInitiationType InitiationType { get; init; }
    public string? SourceAcceptedByAccountCode { get; init; }
    public DateTimeOffset? SourceAcceptedAt { get; init; }
    public string? SourceRejectedByAccountCode { get; init; }
    public DateTimeOffset? SourceRejectedAt { get; init; }
    public string? SourceRejectionReason { get; init; }
    public string? SourceHandoverConfirmedByAccountCode { get; init; }
    public DateTimeOffset? SourceHandoverConfirmedAt { get; init; }
    public string? ReceiverConfirmedAccountCode { get; init; }
    public DateTimeOffset? ReceiverConfirmedAt { get; init; }
    public string? ReceiverRejectedAccountCode { get; init; }
    public DateTimeOffset? ReceiverRejectedAt { get; init; }
    public string? ReceiverRejectionReason { get; init; }
    public required string CreatorAccountCode { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string? DecisionAccountCode { get; init; }
    public DateTimeOffset? DecidedAt { get; init; }
    public string? DecisionReason { get; init; }
    public string? SourceHandoverAccountCode { get; init; }
    public string? TargetHandoverAccountCode { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public string? Notes { get; init; }
    public int ItemCount { get; init; }
    public int Version { get; init; }
}

public sealed class DeviceTransferItemReadModel
{
    public required string DeviceCode { get; init; }
    public required string Name { get; init; }
    public required string CategoryCode { get; init; }
    public required string CategoryName { get; init; }
    public DeviceCondition? ConditionAtHandover { get; init; }
    public DeviceCondition CurrentCondition { get; init; }
    public string? HandoverNote { get; init; }
    public DeviceCondition? ConditionAtSourceHandover { get; init; }
    public string? SourceHandoverNote { get; init; }
}

public sealed class DeviceTransferHistoryReadModel
{
    public required string ActionCode { get; init; }
    public AuditOutcome Outcome { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public string? ActorAccountCode { get; init; }
    public string? ActorFullName { get; init; }
    public string? ReasonCode { get; init; }
    public string? ChangedFields { get; init; }
    public string? BeforeData { get; init; }
    public string? AfterData { get; init; }
}
