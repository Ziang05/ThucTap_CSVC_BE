using CSVC.Core.Manufacturers;
using CSVC.UseCases.Abstractions.Persistence;

namespace CSVC.Infrastructure.Persistence.Repositories;

public sealed class ManufacturerHistoryRepository(CsvcDbContext context)
    : GenericRepository<ManufacturerHistory>(context), IManufacturerHistoryRepository
{
}
