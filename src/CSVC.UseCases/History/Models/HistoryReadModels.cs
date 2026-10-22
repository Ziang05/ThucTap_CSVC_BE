using System.Net;
using CSVC.Core.Common;

namespace CSVC.UseCases.History.Models;

public class HistoryReadModel
{
    public Guid Id { get; init; }
    public Guid RequestId { get; init; }
    public Guid? ActorAccountId { get; init; }
    public string? ActorAccountCode { get; init; }
    public string? ActorFullName { get; init; }
    public string ActionCode { get; init; } = "";
    public AuditOutcome Outcome { get; init; }
    public string? ReasonCode { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public string SearchText { get; init; } = "";
}

public sealed class AccountHistoryReadModel : HistoryReadModel
{
    public Guid? TargetAccountId { get; init; }
    public string? TargetAccountCode { get; init; }
    public string? TargetFullName { get; init; }
    public string? ChangedFields { get; init; }
    public string? BeforeData { get; init; }
    public string? AfterData { get; init; }
}

public sealed class AuthHistoryReadModel : HistoryReadModel
{
    public Guid? AccountId { get; init; }
    public string? AccountCode { get; init; }
    public string? FullName { get; init; }
    public Guid? SessionId { get; init; }
    public string? EmailSubmitted { get; init; }
    public DateTimeOffset? BlockedUntil { get; init; }
    public IPAddress? IpAddress { get; init; }
    public string? UserAgent { get; init; }
}

public sealed class DepartmentHistoryReadModel : HistoryReadModel
{
    public Guid? DepartmentId { get; init; }
    public string? DepartmentCode { get; init; }
    public string? DepartmentName { get; init; }
    public string? BeforeData { get; init; }
    public string? AfterData { get; init; }
}
