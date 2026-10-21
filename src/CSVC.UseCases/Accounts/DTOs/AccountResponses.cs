namespace CSVC.UseCases.Accounts.DTOs;

public sealed class AccountListItemDto
{
    public Guid Id { get; init; }
    public string AccountCode { get; init; } = "";
    public string FullName { get; init; } = "";
    public string Email { get; init; } = "";
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

public sealed class AccountDetailDto
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
}

public sealed record CreatedAccountDto(Guid Id, string AccountCode);
public sealed record PasswordResetAcceptedDto(string Message = "If the account is eligible, password reset instructions will be sent.");
