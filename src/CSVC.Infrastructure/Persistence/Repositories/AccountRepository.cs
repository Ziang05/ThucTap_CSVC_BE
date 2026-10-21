using CSVC.Core.Accounts;
using CSVC.Core.Common;
using CSVC.UseCases.Abstractions.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CSVC.Infrastructure.Persistence.Repositories;

public sealed class AccountRepository(CsvcDbContext context)
    : GenericRepository<Account>(context), IAccountRepository
{
    public Task<Account?> GetByNormalizedEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = TextNormalization.Email(email);
        return FirstOrDefaultAsync(account => EF.Property<string>(account, "NormalizedEmail") == normalizedEmail, cancellationToken);
    }
}
