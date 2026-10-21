namespace CSVC.UseCases.Accounts.Models;

// Safe SQL projection source for AutoMapper: no credentials or token hashes are loaded.
public sealed class AccountReadModel
{
    public Guid Id { get; init; }
    public string AccountCode { get; init; } = "";
    public string FullName { get; init; } = "";
    public string Email { get; init; } = "";
    public DateOnly? BirthDate { get; init; }
    public string? Phone { get; init; }
    public short RoleId { get; init; }
    public string RoleCode { get; init; } = "";
    public string RoleName { get; init; } = "";
    public Guid DepartmentId { get; init; }
    public string DepartmentName { get; init; } = "";
    public bool IsLocked { get; init; }
    public bool HasValidSession { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public Guid? CreatedBy { get; init; }
}
