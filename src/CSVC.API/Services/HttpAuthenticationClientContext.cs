using System.Net;
using CSVC.UseCases.Abstractions.Security;

namespace CSVC.API.Services;

public sealed class HttpAuthenticationClientContext(IHttpContextAccessor accessor) : IAuthenticationClientContext
{
    // Trust the server connection, not arbitrary client-supplied X-Forwarded-For values.
    public IPAddress? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress;
    public string? UserAgent => accessor.HttpContext?.Request.Headers.UserAgent.ToString();
}
