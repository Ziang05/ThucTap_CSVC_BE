using CSVC.Infrastructure.Persistence;
using CSVC.UseCases.Abstractions;
using CSVC.UseCases.Authentication;
using CSVC.UseCases.Abstractions.Security;
using Microsoft.EntityFrameworkCore;

namespace CSVC.Infrastructure.Security;

public sealed class DatabaseSessionValidator(CsvcDbContext context, TimeProvider clock,
    ISessionExpirationService expiration, IRequestContext requestContext) : ISessionValidator
{
    public async Task<SessionIdentity?> ValidateAsync(string token, CancellationToken cancellationToken)
    {
        var hash = SessionToken.Hash(token);
        var now = clock.GetUtcNow();
        var expiredId = await context.AuthSessions.AsNoTracking()
            .Where(session => session.SessionTokenHash == hash && session.IsActive && session.ExpiresAt <= now)
            .Select(session => (Guid?)session.Id).SingleOrDefaultAsync(cancellationToken);
        if (expiredId is { } id)
        {
            await expiration.ExpireSessionAsync(id, requestContext.RequestId, cancellationToken);
            return null;
        }
        return await (from session in context.AuthSessions.AsNoTracking()
                      join account in context.Accounts on session.AccountId equals account.Id
                      join role in context.Roles on account.RoleId equals role.Id
                      where session.SessionTokenHash == hash && session.IsActive && session.ExpiresAt > now && !account.IsLock
                      select new SessionIdentity(account.Id, session.Id, role.Code)).SingleOrDefaultAsync(cancellationToken);
    }
}
