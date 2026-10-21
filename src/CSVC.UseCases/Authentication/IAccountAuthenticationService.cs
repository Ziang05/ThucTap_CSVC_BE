using CSVC.UseCases.Authentication.DTOs;

namespace CSVC.UseCases.Authentication;

public interface IAccountAuthenticationService
{
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
    // Possession of the exact token identifies the session; replay/unknown tokens are harmless no-ops.
    Task LogoutAsync(string? token, CancellationToken cancellationToken = default);
}
