using CSVC.Core.Areas;

namespace CSVC.UseCases.Abstractions.Persistence;

public interface IAreaRepository : IRepository<Area>
{
    Task<IReadOnlyList<Area>> GetChildrenAsync(Guid? parentId, CancellationToken cancellationToken = default);
}
