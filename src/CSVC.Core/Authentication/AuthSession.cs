namespace CSVC.Core.Authentication;

public sealed class AuthSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid AccountId { get; set; }

    public required string SessionTokenHash { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public bool IsActive { get; set; } = true;
}
