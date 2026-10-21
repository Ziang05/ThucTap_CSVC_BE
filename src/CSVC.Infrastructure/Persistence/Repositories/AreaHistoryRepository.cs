using CSVC.Core.Areas;
using CSVC.UseCases.Abstractions.Persistence;

namespace CSVC.Infrastructure.Persistence.Repositories;

public sealed class AreaHistoryRepository(CsvcDbContext context)
    : GenericRepository<AreaHistory>(context), IAreaHistoryRepository
{
}
