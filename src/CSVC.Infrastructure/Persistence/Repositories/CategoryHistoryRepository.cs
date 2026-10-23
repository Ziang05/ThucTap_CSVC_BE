using CSVC.Core.DeviceCategories;
using CSVC.UseCases.Abstractions.Persistence;

namespace CSVC.Infrastructure.Persistence.Repositories;

public sealed class CategoryHistoryRepository(CsvcDbContext context)
    : GenericRepository<CategoryHistory>(context), ICategoryHistoryRepository
{
}
