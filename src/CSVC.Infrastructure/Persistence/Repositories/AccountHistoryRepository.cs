using CSVC.Core.Accounts;
using CSVC.UseCases.Abstractions.Persistence;

namespace CSVC.Infrastructure.Persistence.Repositories;

public sealed class AccountHistoryRepository(CsvcDbContext context)
    : GenericRepository<AccountHistory>(context), IAccountHistoryRepository
{
}
