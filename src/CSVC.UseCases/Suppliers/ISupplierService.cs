using CSVC.UseCases.Common;
using CSVC.UseCases.Suppliers.DTOs;

namespace CSVC.UseCases.Suppliers;

public interface ISupplierService
{
    Task<PagedResult<SupplierListItemDto>> ListAsync(SupplierListQuery query, CancellationToken ct);
    Task<SupplierDetailDto> GetAsync(string displayCode, CancellationToken ct);
    Task<SupplierDetailDto> CreateAsync(CreateSupplierRequest request, CancellationToken ct);
    Task UpdateAsync(string displayCode, UpdateSupplierRequest request, CancellationToken ct);
    Task SetLockedAsync(string displayCode, bool locked, CancellationToken ct);
    Task<SupplierLockImpactDto> GetLockImpactAsync(string displayCode, CancellationToken ct);
    Task DenyDeleteAsync(string displayCode, CancellationToken ct);
    Task RecordInvalidRequestAsync(string? displayCode, string action, CancellationToken ct);
    Task<PagedResult<SupplierHistoryListItemDto>> ListHistoryAsync(SupplierHistoryQuery query, CancellationToken ct, string? scope = null);
    Task<SupplierHistoryDetailDto> GetHistoryAsync(Guid historyId, CancellationToken ct, string? scope = null);
}
