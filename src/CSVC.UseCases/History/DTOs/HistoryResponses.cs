using CSVC.Core.Common;

namespace CSVC.UseCases.History.DTOs;

public class HistoryListItemDto
{
    public Guid Id { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public string ActionCode { get; init; } = "";
    public AuditOutcome Outcome { get; init; }
    public string? ReasonCode { get; init; }
    public Guid? ActorAccountId { get; init; }
    public string? ActorAccountCode { get; init; }
    public string? ActorFullName { get; init; }
}

public class AccountHistoryListItemDto : HistoryListItemDto
{
    public Guid? TargetAccountId { get; init; }
    public string? TargetAccountCode { get; init; }
    public string? TargetFullName { get; init; }
}

public sealed class AccountHistoryDetailDto : AccountHistoryListItemDto
{
    public Guid RequestId { get; init; }
    public string? ChangedFields { get; init; }
    public string? BeforeData { get; init; }
    public string? AfterData { get; init; }
}

public class AuthHistoryListItemDto : HistoryListItemDto
{
    public Guid? AccountId { get; init; }
    public string? AccountCode { get; init; }
    public string? FullName { get; init; }
    public string? EmailSubmitted { get; init; }
}

public sealed class AuthHistoryDetailDto : AuthHistoryListItemDto
{
    public Guid RequestId { get; init; }
    public Guid? SessionId { get; init; }
    public DateTimeOffset? BlockedUntil { get; init; }
    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }
}

public class DepartmentHistoryListItemDto : HistoryListItemDto
{
    public Guid? DepartmentId { get; init; }
    public string? DepartmentCode { get; init; }
    public string? DepartmentName { get; init; }
}

public sealed class DepartmentHistoryDetailDto : DepartmentHistoryListItemDto
{
    public Guid RequestId { get; init; }
    public string? BeforeData { get; init; }
    public string? AfterData { get; init; }
}

public class AreaHistoryListItemDto : HistoryListItemDto
{
    public Guid? AreaId { get; init; }
    public string? AreaCode { get; init; }
    public string? AreaName { get; init; }
}

public sealed class AreaHistoryDetailDto : AreaHistoryListItemDto
{
    public Guid RequestId { get; init; }
    public string? ChangedFields { get; init; }
    public string? BeforeData { get; init; }
    public string? AfterData { get; init; }
}
