using CSVC.Core.DeviceCategories;

namespace CSVC.UseCases.Abstractions.Persistence;

public interface IDeviceCategoryRepository : IRepository<DeviceCategory>
{
    Task<bool> DisplayCodeExistsAsync(string displayCode, Guid? exceptId, CancellationToken cancellationToken);
    Task<DeviceCategory?> GetByDisplayCodeAsync(string displayCode, CancellationToken cancellationToken = default);
    Task<bool> NameExistsAsync(string name, Guid? exceptId, CancellationToken cancellationToken);
}
