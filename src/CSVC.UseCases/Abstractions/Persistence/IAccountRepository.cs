using CSVC.Core.Accounts;
using CSVC.UseCases.Accounts.DTOs;
using CSVC.UseCases.Common;

namespace CSVC.UseCases.Abstractions.Persistence;

public interface IAccountRepository : IRepository<Account>
{
    Task<bool> CodeExistsAsync(string code, Guid? exceptId, CancellationToken cancellationToken);
    Task<PagedResult<AccountListItemDto>> ListAsync(AccountListQuery query, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<AccountDetailDto?> GetDetailAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<int> CountActiveManagersAsync(CancellationToken cancellationToken = default);

    Task<Account?> GetByNormalizedEmailAsync(string email, CancellationToken cancellationToken = default);
}
