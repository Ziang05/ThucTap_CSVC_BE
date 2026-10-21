using CSVC.UseCases.Common;
using CSVC.UseCases.Departments.DTOs;

namespace CSVC.UseCases.Departments;

public interface IDepartmentService
{
    Task<PagedResult<DepartmentListItemDto>> ListAsync(DepartmentListQuery query, CancellationToken cancellationToken);
    Task<DepartmentDetailDto> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<DepartmentDetailDto> CreateAsync(CreateDepartmentRequest request, CancellationToken cancellationToken);
    Task UpdateAsync(Guid id, UpdateDepartmentRequest request, CancellationToken cancellationToken);
    Task SetLockedAsync(Guid id, bool locked, CancellationToken cancellationToken);
    Task<DepartmentLockImpactDto> GetLockImpactAsync(Guid id, CancellationToken cancellationToken);
    Task<PagedResult<DepartmentHistoryDto>> GetHistoryAsync(Guid id, DepartmentHistoryQuery query, CancellationToken cancellationToken);
    Task DenyDeleteAsync(Guid id, CancellationToken cancellationToken);
    Task RecordInvalidRequestAsync(Guid? id, CancellationToken cancellationToken);
}
