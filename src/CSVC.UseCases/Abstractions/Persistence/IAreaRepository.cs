using CSVC.Core.Areas;
using CSVC.UseCases.Areas.DTOs;
using CSVC.UseCases.Common;

namespace CSVC.UseCases.Abstractions.Persistence;

public interface IAreaRepository : IRepository<Area>
{
    Task<IReadOnlyList<Area>> GetChildrenAsync(Guid? parentId, CancellationToken cancellationToken = default);
    Task<bool> NameExistsAsync(string name, Guid? parentId, Guid? exceptId, CancellationToken cancellationToken);
    Task<PagedResult<AreaListItemDto>> ListAsync(AreaListQuery query, Guid? parentId, bool directChildrenOnly, CancellationToken cancellationToken);
    Task<AreaDetailDto?> GetDetailAsync(Guid id, CancellationToken cancellationToken);
    Task<AreaManagementDto> GetManagementAsync(Guid id, CancellationToken cancellationToken);
    Task<int> CountDescendantsAsync(Guid id, CancellationToken cancellationToken);
}
