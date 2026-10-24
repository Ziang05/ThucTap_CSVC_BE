using CSVC.UseCases.Common;
using CSVC.UseCases.Manufacturers.DTOs;

namespace CSVC.UseCases.Manufacturers;

public interface IManufacturerService
{
    Task<PagedResult<ManufacturerListItemDto>> ListAsync(ManufacturerListQuery query, CancellationToken ct);
    Task<ManufacturerDetailDto> GetAsync(string displayCode, CancellationToken ct);
    Task<ManufacturerDetailDto> CreateAsync(CreateManufacturerRequest request, CancellationToken ct);
    Task UpdateAsync(string displayCode, UpdateManufacturerRequest request, CancellationToken ct);
    Task SetLockedAsync(string displayCode, bool locked, CancellationToken ct);
    Task<ManufacturerLockImpactDto> GetLockImpactAsync(string displayCode, CancellationToken ct);
    Task DenyDeleteAsync(string displayCode, CancellationToken ct);
    Task RecordInvalidRequestAsync(string? displayCode, string action, CancellationToken ct);
    Task<PagedResult<ManufacturerHistoryListItemDto>> ListHistoryAsync(ManufacturerHistoryQuery query, CancellationToken ct, string? scope = null);
    Task<ManufacturerHistoryDetailDto> GetHistoryAsync(Guid historyId, CancellationToken ct, string? scope = null);
}
