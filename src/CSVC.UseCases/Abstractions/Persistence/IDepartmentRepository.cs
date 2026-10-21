using CSVC.Core.Departments;

namespace CSVC.UseCases.Abstractions.Persistence;

public interface IDepartmentRepository : IRepository<Department>
{
    Task<Department?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
}
