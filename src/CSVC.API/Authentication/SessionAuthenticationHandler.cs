using System.Security.Claims;
using System.Text.Encodings.Web;
using CSVC.UseCases.Abstractions.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace CSVC.API.Authentication;

public sealed class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    ISessionValidator validator) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DatabaseSession";
    public const string SessionIdClaim = "session_id";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header)) return AuthenticateResult.NoResult();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.Fail("A bearer session token is required.");
        var token = header[7..].Trim();
        if (token.Length is < 32 or > 128 || token.Any(char.IsWhiteSpace))
            return AuthenticateResult.Fail("Invalid session.");
        var identity = await validator.ValidateAsync(token, Context.RequestAborted);
        if (identity is null) return AuthenticateResult.Fail("Invalid session.");
        Claim[] claims =
        [
            new(ClaimTypes.NameIdentifier, identity.AccountId.ToString()),
            new(ClaimTypes.Role, identity.RoleCode),
            new(SessionIdClaim, identity.SessionId.ToString())
        ];
        return AuthenticateResult.Success(new AuthenticationTicket(
            new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = "Bearer";
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }
}
