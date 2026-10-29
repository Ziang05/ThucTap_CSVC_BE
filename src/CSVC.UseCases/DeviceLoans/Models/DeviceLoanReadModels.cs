using CSVC.Core.Common;
using CSVC.Core.DeviceLoans;
using CSVC.Core.Devices;

namespace CSVC.UseCases.DeviceLoans.Models;

public sealed class DeviceLoanReadModel
{
    public Guid Id { get; init; }
    public required string LoanCode { get; init; }
    public required string BorrowerAccountCode { get; init; }
    public required string BorrowerFullName { get; init; }
    public required string DepartmentCode { get; init; }
    public required string DepartmentName { get; init; }
    public required string Purpose { get; init; }
    public DateTimeOffset DueAt { get; init; }
    public DeviceLoanStatus Status { get; init; }
    public required string CreatorAccountCode { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string? DecisionAccountCode { get; init; }
    public DateTimeOffset? DecidedAt { get; init; }
    public string? DecisionReason { get; init; }
    public DateTimeOffset? HandedOverAt { get; init; }
    public string? HandoverAccountCode { get; init; }
    public DateTimeOffset? ReturnedAt { get; init; }
    public string? ReturnAccountCode { get; init; }
    public string? Notes { get; init; }
    public int ItemCount { get; init; }
    public int Version { get; init; }
    public bool IsOverdue { get; init; }
}

public sealed class DeviceLoanItemReadModel
{
    public required string DeviceCode { get; init; }
    public required string Name { get; init; }
    public required string CategoryCode { get; init; }
    public required string CategoryName { get; init; }
    public DeviceCondition? ConditionAtHandover { get; init; }
    public string? HandoverNote { get; init; }
    public DeviceCondition? ConditionAtReturn { get; init; }
    public string? ReturnNote { get; init; }
}

public sealed class DeviceLoanHistoryReadModel
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
