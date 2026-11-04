using CSVC.Core.Devices;
using CSVC.UseCases.Common;

namespace CSVC.UseCases.DeviceLoans.DTOs;

public sealed record DeviceLoanItemDto(string DeviceCode, string Name, string CategoryCode, string CategoryName,
    DeviceCondition? ConditionAtHandover, string? HandoverNote, DeviceCondition? ConditionAtReturn, string? ReturnNote)
{
    public DeviceCondition? DeclaredReturnCondition { get; init; }
    public string? DeclaredReturnNote { get; init; }
}

public sealed record DeviceLoanDto(string LoanCode, string BorrowerAccountCode, string BorrowerFullName,
    string DepartmentCode, string DepartmentName, string Purpose, DateTimeOffset DueAt, string Status, bool IsOverdue,
    string CreatorAccountCode, DateTimeOffset CreatedAt, string? DecisionAccountCode, DateTimeOffset? DecidedAt,
    string? DecisionReason, DateTimeOffset? HandedOverAt, string? HandoverAccountCode,
    DateTimeOffset? ReturnedAt, string? ReturnAccountCode, string? Notes, int ItemCount, int Version,
    IReadOnlyList<DeviceLoanItemDto>? Items = null)
{
    public string? LenderDepartmentCode { get; init; }
    public string? LenderDepartmentName { get; init; }
    public int WorkflowVersion { get; init; }
    public string WorkflowStatus { get; init; } = "";
    public DateTimeOffset? ReturnRequestedAt { get; init; }
    public string? ReturnRequestedByAccountCode { get; init; }
    public string? LenderConfirmedBy => WorkflowVersion == 2 ? DecisionAccountCode : null;
    public DateTimeOffset? LenderConfirmedAt => WorkflowVersion == 2 ? DecidedAt : null;
    public string? LenderDecisionReason => WorkflowVersion == 2 ? DecisionReason : null;
}

public sealed record LoanDeviceOptionDto(string DeviceCode, string Name, string CategoryCode, string CategoryName,
    string DepartmentCode, string DepartmentName, string? SerialNumber, string? ParentDeviceCode, string? ParentDeviceName,
    DeviceCondition Condition, bool IsComponent);

public sealed record DeviceLoanHistoryDto(string ActionCode, string Outcome, DateTimeOffset OccurredAt,
    string? ActorAccountCode, string? ActorFullName, string? ReasonCode, string? ChangedFields,
    string? BeforeData, string? AfterData);
