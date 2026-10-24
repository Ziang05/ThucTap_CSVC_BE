using CSVC.Core.Suppliers;
using CSVC.UseCases.Common;
using CSVC.UseCases.Suppliers.DTOs;

namespace CSVC.UseCases.Abstractions.Persistence;

public interface ISupplierRepository : IRepository<Supplier>
{
    Task<PagedResult<SupplierListItemDto>> ListAsync(SupplierListQuery query, CancellationToken ct);
    Task<SupplierManagementDto> GetManagementAsync(Guid id, CancellationToken ct);
    Task<bool> DisplayCodeExistsAsync(string displayCode, Guid? exceptId, CancellationToken cancellationToken);
    Task<Supplier?> GetByDisplayCodeAsync(string displayCode, CancellationToken cancellationToken = default);
    Task<bool> TaxCodeExistsAsync(string taxCode, Guid? exceptId, CancellationToken cancellationToken);
    Task<bool> NameExistsAsync(string name, Guid? exceptId, CancellationToken cancellationToken);
}
