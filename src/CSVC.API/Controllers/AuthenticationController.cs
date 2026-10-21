using System.Net.Http.Headers;
using CSVC.UseCases.Authentication;
using CSVC.UseCases.Authentication.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CSVC.API.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/v1/auth")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AuthenticationController(IAccountAuthenticationService authentication) : ControllerBase
{
    [HttpPost("login")]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
        => Ok(await authentication.LoginAsync(request, cancellationToken));

    // Deliberately idempotent: an expired/revoked/unknown token must not create another end event.
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var header = Request.Headers.Authorization.ToString();
        var token = AuthenticationHeaderValue.TryParse(header, out var parsed)
                    && parsed.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase) ? parsed.Parameter : null;
        await authentication.LogoutAsync(token, cancellationToken);
        return NoContent();
    }
}
