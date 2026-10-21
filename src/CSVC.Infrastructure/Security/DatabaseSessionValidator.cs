using System.Security.Cryptography;
using System.Text;
using CSVC.Infrastructure.Persistence;
using CSVC.UseCases.Abstractions.Security;
using Microsoft.EntityFrameworkCore;

namespace CSVC.Infrastructure.Security;

public sealed class DatabaseSessionValidator(CsvcDbContext context, TimeProvider clock) : ISessionValidator
{
    public async Task<SessionIdentity?> ValidateAsync(string token, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        var now = clock.GetUtcNow();
        return await (from session in context.AuthSessions.AsNoTracking()
                      join account in context.Accounts on session.AccountId equals account.Id
                      join role in context.Roles on account.RoleId equals role.Id
                      where session.SessionTokenHash == hash && session.IsActive && session.ExpiresAt > now && !account.IsLock
                      select new SessionIdentity(account.Id, session.Id, role.Code)).SingleOrDefaultAsync(cancellationToken);
    }
}
