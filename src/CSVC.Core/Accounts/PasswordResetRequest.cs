namespace CSVC.Core.Accounts;

public sealed class PasswordResetRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid AccountId { get; set; }

    public required string TokenHash { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public bool IsClosed { get; set; } = false;
}
