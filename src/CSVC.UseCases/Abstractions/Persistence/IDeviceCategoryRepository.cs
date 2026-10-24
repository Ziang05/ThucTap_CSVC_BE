using CSVC.Core.DeviceCategories;
using CSVC.UseCases.Common;
using CSVC.UseCases.DeviceCategories.DTOs;

namespace CSVC.UseCases.Abstractions.Persistence;

public interface IDeviceCategoryRepository : IRepository<DeviceCategory>
{
    Task<PagedResult<CategoryListItemDto>> ListAsync(CategoryListQuery query, CancellationToken ct);
    Task<CategoryManagementDto> GetManagementAsync(Guid id, CancellationToken ct);
    Task<bool> DisplayCodeExistsAsync(string displayCode, Guid? exceptId, CancellationToken cancellationToken);
    Task<DeviceCategory?> GetByDisplayCodeAsync(string displayCode, CancellationToken cancellationToken = default);
    Task<bool> NameExistsAsync(string name, Guid? exceptId, CancellationToken cancellationToken);
}
