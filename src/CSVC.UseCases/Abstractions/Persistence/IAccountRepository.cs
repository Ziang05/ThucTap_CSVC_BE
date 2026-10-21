using CSVC.Core.Accounts;

namespace CSVC.UseCases.Abstractions.Persistence;

public interface IAccountRepository : IRepository<Account>
{
    Task<Account?> GetByNormalizedEmailAsync(string email, CancellationToken cancellationToken = default);
}
