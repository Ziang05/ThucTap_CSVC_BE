using CSVC.Core.Manufacturers;
using CSVC.UseCases.Common;
using CSVC.UseCases.Manufacturers.DTOs;

namespace CSVC.UseCases.Abstractions.Persistence;

public interface IManufacturerRepository : IRepository<Manufacturer>
{
    Task<PagedResult<ManufacturerListItemDto>> ListAsync(ManufacturerListQuery query, CancellationToken ct);
    Task<ManufacturerManagementDto> GetManagementAsync(Guid id, CancellationToken ct);
    Task<bool> DisplayCodeExistsAsync(string displayCode, Guid? exceptId, CancellationToken cancellationToken);
    Task<Manufacturer?> GetByDisplayCodeAsync(string displayCode, CancellationToken cancellationToken = default);
    Task<bool> NameExistsAsync(string name, Guid? exceptId, CancellationToken cancellationToken);
}
