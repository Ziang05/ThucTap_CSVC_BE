using CSVC.Core.Areas;
using CSVC.UseCases.Abstractions.Persistence;

namespace CSVC.Infrastructure.Persistence.Repositories;

public sealed class AreaRepository(CsvcDbContext context)
    : GenericRepository<Area>(context), IAreaRepository
{
    public Task<IReadOnlyList<Area>> GetChildrenAsync(Guid? parentId, CancellationToken cancellationToken = default)
        => FindAsync(area => area.ParentId == parentId, cancellationToken);
}
