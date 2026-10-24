using CSVC.Core.DeviceCategories;
using CSVC.UseCases.Common;
using CSVC.UseCases.DeviceCategories.DTOs;
using CSVC.UseCases.DeviceCategories.Models;

namespace CSVC.UseCases.Abstractions.Persistence;

public interface ICategoryHistoryRepository : IRepository<CategoryHistory>
{
    Task<PagedResult<CategoryHistoryListItemDto>> ListAsync(CategoryHistoryQuery query, CancellationToken ct, Guid? scope = null);
    Task<CategoryHistoryDetailReadModel?> GetDetailAsync(Guid id, CancellationToken ct, Guid? scope = null);
}
