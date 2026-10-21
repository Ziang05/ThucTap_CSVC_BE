using CSVC.Core.Departments;
using CSVC.UseCases.Abstractions.Persistence;

namespace CSVC.Infrastructure.Persistence.Repositories;

public sealed class DepartmentRepository(CsvcDbContext context)
    : GenericRepository<Department>(context), IDepartmentRepository
{
    public Task<Department?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
        => FirstOrDefaultAsync(department => department.Code == code, cancellationToken);
}
