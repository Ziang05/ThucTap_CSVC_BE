using CSVC.Core.Authentication;

namespace CSVC.UseCases.Abstractions.Persistence;

public interface IAuthSessionRepository : IRepository<AuthSession>
{
    Task<IReadOnlyList<AuthSession>> GetExpiredAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken = default);

    Task<AuthSession?> GetActiveByAccountIdAsync(Guid accountId, DateTimeOffset now, CancellationToken cancellationToken = default);
}
