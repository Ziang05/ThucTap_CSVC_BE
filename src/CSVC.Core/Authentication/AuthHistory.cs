using System.Net;
using CSVC.Core.Common;

namespace CSVC.Core.Authentication;

public sealed class AuthHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RequestId { get; set; }

    public Guid? ActorAccountId { get; set; }

    public required string ActionCode { get; set; }

    public required AuditOutcome Outcome { get; set; }

    public string? ReasonCode { get; set; }

    public required DateTimeOffset OccurredAt { get; set; }

    public Guid? AccountId { get; set; }

    public Guid? SessionId { get; set; }

    public string? EmailSubmitted { get; set; }

    public DateTimeOffset? BlockedUntil { get; set; }

    public IPAddress? IpAddress { get; set; }

    public string? UserAgent { get; set; }
}
