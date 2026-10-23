using CSVC.Core.Suppliers;

namespace CSVC.UseCases.Abstractions.Persistence;

public interface ISupplierRepository : IRepository<Supplier>
{
    Task<bool> DisplayCodeExistsAsync(string displayCode, Guid? exceptId, CancellationToken cancellationToken);
    Task<Supplier?> GetByDisplayCodeAsync(string displayCode, CancellationToken cancellationToken = default);
    Task<bool> NameExistsAsync(string name, Guid? exceptId, CancellationToken cancellationToken);
}
