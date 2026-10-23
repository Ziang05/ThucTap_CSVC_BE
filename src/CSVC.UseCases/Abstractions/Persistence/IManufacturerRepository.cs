using CSVC.Core.Manufacturers;

namespace CSVC.UseCases.Abstractions.Persistence;

public interface IManufacturerRepository : IRepository<Manufacturer>
{
    Task<bool> DisplayCodeExistsAsync(string displayCode, Guid? exceptId, CancellationToken cancellationToken);
    Task<Manufacturer?> GetByDisplayCodeAsync(string displayCode, CancellationToken cancellationToken = default);
    Task<bool> NameExistsAsync(string name, Guid? exceptId, CancellationToken cancellationToken);
}
