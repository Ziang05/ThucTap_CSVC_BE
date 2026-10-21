using CSVC.Core.Authentication;

namespace CSVC.UseCases.Abstractions.Persistence;

public interface IAuthSessionRepository : IRepository<AuthSession>
{
    Task<AuthSession?> GetActiveByAccountIdAsync(Guid accountId, DateTimeOffset now, CancellationToken cancellationToken = default);
}
