using CSVC.Core.Manufacturers;
using CSVC.UseCases.Common;
using CSVC.UseCases.Manufacturers.DTOs;
using CSVC.UseCases.Manufacturers.Models;

namespace CSVC.UseCases.Abstractions.Persistence;

public interface IManufacturerHistoryRepository : IRepository<ManufacturerHistory>
{
    Task<PagedResult<ManufacturerHistoryListItemDto>> ListAsync(ManufacturerHistoryQuery query, CancellationToken ct, Guid? scope = null);
    Task<ManufacturerHistoryDetailReadModel?> GetDetailAsync(Guid id, CancellationToken ct, Guid? scope = null);
}
