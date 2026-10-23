using CSVC.Core.DeviceCategories;
using CSVC.Core.Common;
using CSVC.UseCases.Abstractions.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CSVC.Infrastructure.Persistence.Repositories;

public sealed class DeviceCategoryRepository(CsvcDbContext context)
    : GenericRepository<DeviceCategory>(context), IDeviceCategoryRepository
{
    public Task<bool> DisplayCodeExistsAsync(string displayCode, Guid? exceptId, CancellationToken cancellationToken)
    {
        var key = displayCode.Trim().ToLowerInvariant();
        return _dbSet.AnyAsync(entity => entity.Id != exceptId
            && EF.Property<string>(entity, "NormalizedDisplayCode") == key, cancellationToken);
    }

    public Task<DeviceCategory?> GetByDisplayCodeAsync(string displayCode, CancellationToken cancellationToken = default)
    {
        var key = displayCode.Trim().ToLowerInvariant();
        return FirstOrDefaultAsync(entity => EF.Property<string>(entity, "NormalizedDisplayCode") == key, cancellationToken);
    }

    public Task<bool> NameExistsAsync(string name, Guid? exceptId, CancellationToken cancellationToken)
    {
        var key = TextNormalization.Name(name).ToLowerInvariant();
        return _dbSet.AnyAsync(entity => entity.Id != exceptId
            && EF.Property<string>(entity, "NormalizedName") == key, cancellationToken);
    }
}
