using CSVC.Core.Departments;
using CSVC.UseCases.Abstractions.Persistence;

namespace CSVC.Infrastructure.Persistence.Repositories;

public sealed class DepartmentHistoryRepository(CsvcDbContext context)
    : GenericRepository<DepartmentHistory>(context), IDepartmentHistoryRepository
{
}
