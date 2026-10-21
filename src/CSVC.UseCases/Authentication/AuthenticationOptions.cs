using System.ComponentModel.DataAnnotations;

namespace CSVC.UseCases.Authentication;

public sealed class AuthenticationOptions
{
    public const string SectionName = "Authentication";
    [Range(1, 10_080)] public int SessionLifetimeMinutes { get; set; } = 480;
    public bool ExpirationEnabled { get; set; } = true;
    [Range(5, 3600)] public int ExpirationIntervalSeconds { get; set; } = 60;
    [Range(1, 1000)] public int ExpirationBatchSize { get; set; } = 100;
}
