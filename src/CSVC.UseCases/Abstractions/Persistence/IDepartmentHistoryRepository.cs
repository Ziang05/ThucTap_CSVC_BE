using CSVC.Core.Departments;
using CSVC.UseCases.Common;
using CSVC.UseCases.History.DTOs;

namespace CSVC.UseCases.Abstractions.Persistence;

public interface IDepartmentHistoryRepository : IRepository<DepartmentHistory>
{
    Task<PagedResult<DepartmentHistoryListItemDto>> ListAsync(DepartmentHistoryQuery query, CancellationToken ct, Guid? departmentScope = null);
    Task<DepartmentHistoryDetailDto?> GetDetailAsync(Guid id, CancellationToken ct, Guid? departmentScope = null);
}
