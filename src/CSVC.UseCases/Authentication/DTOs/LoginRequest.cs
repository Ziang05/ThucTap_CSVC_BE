using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace CSVC.UseCases.Authentication.DTOs;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class LoginRequest
{
    private string _email = "";

    [Required, EmailAddress, StringLength(320)]
    public string Email { get => _email; init => _email = value?.Trim() ?? ""; }

    // Existing/initial passwords may be longer than the policy for setting a new password.
    [Required, StringLength(1024)] public string Password { get; init; } = "";
}
