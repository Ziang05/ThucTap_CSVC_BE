using CSVC.Core.Areas;
using CSVC.UseCases.Areas.DTOs;
using CSVC.UseCases.Common;

namespace CSVC.UseCases.Abstractions.Persistence;

public interface IAreaHistoryRepository : IRepository<AreaHistory>
{
    Task<PagedResult<AreaHistoryDto>> ListAsync(Guid id, AreaHistoryQuery query, CancellationToken cancellationToken);
}
