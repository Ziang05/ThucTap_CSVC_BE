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
    public string? HandoverNote { get; init; }
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
