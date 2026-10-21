namespace CSVC.Core.Accounts;

public sealed class Account
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string AccountCode { get; set; }

    public Guid UserProfileId { get; set; }

    public short RoleId { get; set; }

    public required string Email { get; set; }

    public required string PasswordHash { get; set; }

    public bool IsLock { get; set; } = false;
}
