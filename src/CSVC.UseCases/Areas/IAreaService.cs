using CSVC.UseCases.Areas.DTOs;
using CSVC.UseCases.Common;

namespace CSVC.UseCases.Areas;

public interface IAreaService
{
    Task<PagedResult<AreaListItemDto>> ListAsync(AreaListQuery query, CancellationToken cancellationToken);
    Task<PagedResult<AreaListItemDto>> GetChildrenAsync(Guid id, AreaListQuery query, CancellationToken cancellationToken);
    Task<AreaDetailDto> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<AreaDetailDto> CreateAsync(CreateAreaRequest request, CancellationToken cancellationToken);
    Task UpdateAsync(Guid id, UpdateAreaRequest request, CancellationToken cancellationToken);
    Task SetLockedAsync(Guid id, bool locked, CancellationToken cancellationToken);
    Task<AreaLockImpactDto> GetLockImpactAsync(Guid id, CancellationToken cancellationToken);
    Task<PagedResult<AreaHistoryDto>> GetHistoryAsync(Guid id, AreaHistoryQuery query, CancellationToken cancellationToken);
    Task DenyDeleteAsync(Guid id, CancellationToken cancellationToken);
    Task RecordInvalidRequestAsync(Guid? id, CancellationToken cancellationToken);
}
