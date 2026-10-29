using CSVC.Core.DeviceTransfers;
using CSVC.Core.Devices;
using CSVC.UseCases.Common;

namespace CSVC.UseCases.DeviceTransfers.DTOs;

public sealed record DeviceTransferItemDto(string DeviceCode, string Name, string CategoryCode, string CategoryName,
    DeviceCondition? ConditionAtHandover, string? HandoverNote);

public sealed record DeviceTransferDto(string TransferCode, string SourceDepartmentCode, string SourceDepartmentName,
    string TargetDepartmentCode, string TargetDepartmentName, string TargetResponsibleAccountCode,
    string TargetResponsibleFullName, string Reason, DeviceTransferStatus Status, string CreatorAccountCode,
    DateTimeOffset CreatedAt, string? DecisionAccountCode, DateTimeOffset? DecidedAt, string? DecisionReason,
    string? SourceHandoverAccountCode, string? TargetHandoverAccountCode, DateTimeOffset? CompletedAt,
    string? Notes, int ItemCount, int Version, IReadOnlyList<DeviceTransferItemDto>? Items = null);

public sealed record DeviceTransferHistoryDto(string ActionCode, string Outcome, DateTimeOffset OccurredAt,
    string? ActorAccountCode, string? ActorFullName, string? ReasonCode, string? ChangedFields,
    string? BeforeData, string? AfterData);
