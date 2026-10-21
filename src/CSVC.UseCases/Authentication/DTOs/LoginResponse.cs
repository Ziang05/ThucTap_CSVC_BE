namespace CSVC.UseCases.Authentication.DTOs;

// Class rather than record: do not auto-generate a ToString containing the bearer credential.
public sealed class LoginResponse
{
    public string AccessToken { get; init; } = "";
    public string TokenType => "Bearer";
    public DateTimeOffset ExpiresAt { get; init; }
    public AuthenticatedUserDto User { get; init; } = new();
}

public sealed class AuthenticatedUserDto
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
}
