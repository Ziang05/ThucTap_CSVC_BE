using CSVC.UseCases.Common;
using CSVC.UseCases.DeviceCategories.DTOs;

namespace CSVC.UseCases.DeviceCategories;

public interface ICategoryService
{
    Task<PagedResult<CategoryListItemDto>> ListAsync(CategoryListQuery query, CancellationToken ct);
    Task<CategoryDetailDto> GetAsync(string displayCode, CancellationToken ct);
    Task<CategoryDetailDto> CreateAsync(CreateCategoryRequest request, CancellationToken ct);
    Task UpdateAsync(string displayCode, UpdateCategoryRequest request, CancellationToken ct);
    Task SetLockedAsync(string displayCode, bool locked, CancellationToken ct);
    Task<CategoryLockImpactDto> GetLockImpactAsync(string displayCode, CancellationToken ct);
    Task DenyDeleteAsync(string displayCode, CancellationToken ct);
    Task RecordInvalidRequestAsync(string? displayCode, string action, CancellationToken ct);
    Task<PagedResult<CategoryHistoryListItemDto>> ListHistoryAsync(CategoryHistoryQuery query, CancellationToken ct, string? scope = null);
    Task<CategoryHistoryDetailDto> GetHistoryAsync(Guid historyId, CancellationToken ct, string? scope = null);
}
