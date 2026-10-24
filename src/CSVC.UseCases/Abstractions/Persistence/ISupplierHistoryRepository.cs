using CSVC.Core.Suppliers;
using CSVC.UseCases.Common;
using CSVC.UseCases.Suppliers.DTOs;
using CSVC.UseCases.Suppliers.Models;

namespace CSVC.UseCases.Abstractions.Persistence;

public interface ISupplierHistoryRepository : IRepository<SupplierHistory>
{
    Task<PagedResult<SupplierHistoryListItemDto>> ListAsync(SupplierHistoryQuery query, CancellationToken ct, Guid? scope = null);
    Task<SupplierHistoryDetailReadModel?> GetDetailAsync(Guid id, CancellationToken ct, Guid? scope = null);
}
