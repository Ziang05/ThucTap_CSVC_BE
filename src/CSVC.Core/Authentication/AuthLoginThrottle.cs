namespace CSVC.Core.Authentication;

public sealed class AuthLoginThrottle
{
    public required string EmailKey { get; set; }

    public int FailedCount { get; set; } = 0;

    public DateTimeOffset? FirstFailedAt { get; set; }

    public DateTimeOffset? BlockedUntil { get; set; }
}
