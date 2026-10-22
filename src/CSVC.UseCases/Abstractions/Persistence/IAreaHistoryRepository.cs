using CSVC.Core.Areas;
using CSVC.UseCases.History.DTOs;
using CSVC.UseCases.Common;

namespace CSVC.UseCases.Abstractions.Persistence;

public interface IAreaHistoryRepository : IRepository<AreaHistory>
{
    Task<PagedResult<AreaHistoryListItemDto>> ListAsync(AreaHistoryQuery query, CancellationToken ct, Guid? areaScope = null);
    Task<AreaHistoryDetailDto?> GetDetailAsync(Guid id, CancellationToken ct, Guid? areaScope = null, bool includeDescendants = false);
}
