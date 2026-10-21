using System.Security.Claims;
using CSVC.API.Authentication;
using CSVC.UseCases.Abstractions.Security;

namespace CSVC.API.Services;

public sealed class HttpCurrentAccount(IHttpContextAccessor accessor) : ICurrentAccount
{
    public Guid? AccountId => Read(ClaimTypes.NameIdentifier);
    public Guid? SessionId => Read(SessionAuthenticationHandler.SessionIdClaim);

    private Guid? Read(string claim)
        => accessor.HttpContext?.User.Identity?.IsAuthenticated == true
           && Guid.TryParse(accessor.HttpContext.User.FindFirstValue(claim), out var id) ? id : null;
}
