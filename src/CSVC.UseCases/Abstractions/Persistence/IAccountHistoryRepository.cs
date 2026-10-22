using CSVC.Core.Accounts;
using CSVC.UseCases.Common;
using CSVC.UseCases.History.DTOs;

namespace CSVC.UseCases.Abstractions.Persistence;

public interface IAccountHistoryRepository : IRepository<AccountHistory>
{
    Task<PagedResult<AccountHistoryListItemDto>> ListAsync(AccountHistoryQuery query, CancellationToken ct);
    Task<AccountHistoryDetailDto?> GetDetailAsync(Guid id, CancellationToken ct);
}
