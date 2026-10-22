using CSVC.Core.Authentication;
using CSVC.UseCases.Common;
using CSVC.UseCases.History.DTOs;

namespace CSVC.UseCases.Abstractions.Persistence;

public interface IAuthHistoryRepository : IRepository<AuthHistory>
{
    Task<PagedResult<AuthHistoryListItemDto>> ListAsync(AuthHistoryQuery query, CancellationToken ct);
    Task<AuthHistoryDetailDto?> GetDetailAsync(Guid id, CancellationToken ct);
}
