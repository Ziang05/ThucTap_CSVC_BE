using CSVC.Core.Authentication;
using Microsoft.EntityFrameworkCore;
using CSVC.UseCases.Abstractions.Persistence;

namespace CSVC.Infrastructure.Persistence.Repositories;

public sealed class AuthSessionRepository(CsvcDbContext context)
    : GenericRepository<AuthSession>(context), IAuthSessionRepository
{
    public async Task<IReadOnlyList<AuthSession>> GetExpiredAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken = default)
        => await _dbSet.AsNoTracking().Where(session => session.IsActive && session.ExpiresAt <= now)
            .OrderBy(session => session.ExpiresAt).ThenBy(session => session.Id).Take(limit).ToListAsync(cancellationToken);

    public Task<AuthSession?> GetActiveByAccountIdAsync(Guid accountId, DateTimeOffset now, CancellationToken cancellationToken = default)
        => FirstOrDefaultAsync(session => session.AccountId == accountId && session.IsActive && session.ExpiresAt > now,
            cancellationToken);
}
