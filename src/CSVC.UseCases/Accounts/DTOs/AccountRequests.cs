using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace CSVC.UseCases.Accounts.DTOs;

// Unknown fields are rejected so callers cannot silently submit role/email/lock changes to an update.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateAccountRequest
{
    [Required, StringLength(200)] public string FullName { get; init; } = "";
    [Required, EmailAddress, StringLength(320)] public string Email { get; init; } = "";
    [StringLength(32)] public string? Phone { get; init; }
    [Range(1, short.MaxValue)] public short RoleId { get; init; }
    public Guid DepartmentId { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdateAccountProfileRequest
{
    [Required, StringLength(200)] public string FullName { get; init; } = "";
    public DateOnly? BirthDate { get; init; }
    [StringLength(32)] public string? Phone { get; init; }
    // null keeps the current department; only a manager may supply a department.
    public Guid? DepartmentId { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ChangePasswordRequest
{
    [Required, StringLength(1024)] public string CurrentPassword { get; init; } = "";
    [Required, StringLength(128, MinimumLength = 12)] public string NewPassword { get; init; } = "";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class SetAccountPasswordRequest
{
    [Required, StringLength(128, MinimumLength = 12)] public string NewPassword { get; init; } = "";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RequestPasswordResetRequest
{
    [Required, EmailAddress, StringLength(320)] public string Email { get; init; } = "";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CompletePasswordResetRequest
{
    [Required, StringLength(128)] public string Token { get; init; } = "";
    [Required, StringLength(128, MinimumLength = 12)] public string NewPassword { get; init; } = "";
}

public enum AccountSortBy { FullName, CreatedAt }
public enum SortDirection { Asc, Desc }

public sealed class AccountListQuery
{
    [StringLength(200)] public string? Search { get; init; }
    [Range(1, short.MaxValue)] public short? RoleId { get; init; }
    public bool? IsLocked { get; init; }
    [EnumDataType(typeof(AccountSortBy))] public AccountSortBy SortBy { get; init; } = AccountSortBy.FullName;
    [EnumDataType(typeof(SortDirection))] public SortDirection SortDirection { get; init; } = SortDirection.Asc;
    [Range(1, 1_000_000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
}
