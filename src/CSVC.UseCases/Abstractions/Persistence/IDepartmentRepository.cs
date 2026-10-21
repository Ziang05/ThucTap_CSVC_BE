using CSVC.Core.Departments;
using CSVC.UseCases.Common;
using CSVC.UseCases.Departments.DTOs;

namespace CSVC.UseCases.Abstractions.Persistence;

public interface IDepartmentRepository : IRepository<Department>
{
    Task<Department?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<bool> NameExistsAsync(string name, Guid? exceptId, CancellationToken cancellationToken);
    Task<PagedResult<DepartmentListItemDto>> ListAsync(DepartmentListQuery query, CancellationToken cancellationToken);
    Task<DepartmentDetailDto?> GetDetailAsync(Guid id, CancellationToken cancellationToken);
    Task<DepartmentManagementDto> GetManagementAsync(Guid id, CancellationToken cancellationToken);
}
