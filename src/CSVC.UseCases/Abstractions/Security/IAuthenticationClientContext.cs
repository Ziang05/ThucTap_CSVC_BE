using System.Net;

namespace CSVC.UseCases.Abstractions.Security;

public interface IAuthenticationClientContext
{
    IPAddress? IpAddress { get; }
    string? UserAgent { get; }
}
