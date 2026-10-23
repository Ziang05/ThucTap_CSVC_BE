using CSVC.Core.Suppliers;
using CSVC.Core.Common;
using CSVC.UseCases.Abstractions.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CSVC.Infrastructure.Persistence.Repositories;

public sealed class SupplierRepository(CsvcDbContext context)
    : GenericRepository<Supplier>(context), ISupplierRepository
{
    public Task<bool> DisplayCodeExistsAsync(string displayCode, Guid? exceptId, CancellationToken cancellationToken)
    {
        var key = displayCode.Trim().ToLowerInvariant();
        return _dbSet.AnyAsync(entity => entity.Id != exceptId
            && EF.Property<string>(entity, "NormalizedDisplayCode") == key, cancellationToken);
    }

    public Task<Supplier?> GetByDisplayCodeAsync(string displayCode, CancellationToken cancellationToken = default)
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
