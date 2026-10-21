namespace CSVC.UseCases.Abstractions.Security;

public sealed record SessionIdentity(Guid AccountId, Guid SessionId, string RoleCode);

public interface ISessionValidator
{
    Task<SessionIdentity?> ValidateAsync(string token, CancellationToken cancellationToken);
}
