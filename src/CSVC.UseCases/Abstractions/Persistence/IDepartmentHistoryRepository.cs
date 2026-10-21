using CSVC.Core.Departments;
using CSVC.UseCases.Common;
using CSVC.UseCases.Departments.DTOs;

namespace CSVC.UseCases.Abstractions.Persistence;

public interface IDepartmentHistoryRepository : IRepository<DepartmentHistory>
{
    Task<PagedResult<DepartmentHistoryDto>> ListAsync(Guid departmentId, DepartmentHistoryQuery query, CancellationToken cancellationToken);
}
