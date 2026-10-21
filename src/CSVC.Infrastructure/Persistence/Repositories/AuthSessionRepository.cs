using CSVC.Core.Authentication;
using CSVC.UseCases.Abstractions.Persistence;

namespace CSVC.Infrastructure.Persistence.Repositories;

public sealed class AuthSessionRepository(CsvcDbContext context)
    : GenericRepository<AuthSession>(context), IAuthSessionRepository
{
    public Task<AuthSession?> GetActiveByAccountIdAsync(Guid accountId, DateTimeOffset now, CancellationToken cancellationToken = default)
        => FirstOrDefaultAsync(session => session.AccountId == accountId && session.IsActive && session.ExpiresAt > now,
            cancellationToken);
}
