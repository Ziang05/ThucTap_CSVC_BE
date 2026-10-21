namespace CSVC.Core.Accounts;

public sealed class UserProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid DepartmentId { get; set; }

    public required string FullName { get; set; }

    public DateOnly? BirthDate { get; set; }

    public string? Phone { get; set; }
}
