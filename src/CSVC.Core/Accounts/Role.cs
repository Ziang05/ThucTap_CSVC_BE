namespace CSVC.Core.Accounts;

public sealed class Role
{
    public short Id { get; set; }

    public required string Code { get; set; }

    public required string DisplayName { get; set; }
}
