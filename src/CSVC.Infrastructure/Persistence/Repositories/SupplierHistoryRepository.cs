using CSVC.Core.Suppliers;
using CSVC.UseCases.Abstractions.Persistence;

namespace CSVC.Infrastructure.Persistence.Repositories;

public sealed class SupplierHistoryRepository(CsvcDbContext context)
    : GenericRepository<SupplierHistory>(context), ISupplierHistoryRepository
{
}
