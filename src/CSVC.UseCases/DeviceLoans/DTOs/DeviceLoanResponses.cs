using CSVC.Core.Devices;
using CSVC.UseCases.Common;

namespace CSVC.UseCases.DeviceLoans.DTOs;

public sealed record DeviceLoanItemDto(string DeviceCode, string Name, string CategoryCode, string CategoryName,
    DeviceCondition? ConditionAtHandover, string? HandoverNote, DeviceCondition? ConditionAtReturn, string? ReturnNote);

public sealed record DeviceLoanDto(string LoanCode, string BorrowerAccountCode, string BorrowerFullName,
    string DepartmentCode, string DepartmentName, string Purpose, DateTimeOffset DueAt, string Status, bool IsOverdue,
    string CreatorAccountCode, DateTimeOffset CreatedAt, string? DecisionAccountCode, DateTimeOffset? DecidedAt,
    string? DecisionReason, DateTimeOffset? HandedOverAt, string? HandoverAccountCode,
    DateTimeOffset? ReturnedAt, string? ReturnAccountCode, string? Notes, int ItemCount, int Version,
    IReadOnlyList<DeviceLoanItemDto>? Items = null);

public sealed record DeviceLoanHistoryDto(string ActionCode, string Outcome, DateTimeOffset OccurredAt,
    string? ActorAccountCode, string? ActorFullName, string? ReasonCode, string? ChangedFields,
    string? BeforeData, string? AfterData);
