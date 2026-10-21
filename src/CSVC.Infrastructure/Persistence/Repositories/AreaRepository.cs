using CSVC.Core.Areas;
using CSVC.UseCases.Abstractions.Persistence;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using CSVC.Core.Common;
using CSVC.UseCases.Areas.DTOs;
using CSVC.UseCases.Common;
using Microsoft.EntityFrameworkCore;

namespace CSVC.Infrastructure.Persistence.Repositories;

public sealed class AreaRepository(CsvcDbContext context, IMapper mapper)
    : GenericRepository<Area>(context), IAreaRepository
{
    public Task<IReadOnlyList<Area>> GetChildrenAsync(Guid? parentId, CancellationToken cancellationToken = default)
        => FindAsync(area => area.ParentId == parentId, cancellationToken);

    public Task<bool> NameExistsAsync(string name, Guid? parentId, Guid? exceptId, CancellationToken cancellationToken)
    {
        var key = TextNormalization.Name(name).ToLowerInvariant();
        return _dbSet.AnyAsync(a => a.ParentId == parentId && a.Id != exceptId
            && EF.Property<string>(a, "NormalizedName") == key, cancellationToken);
    }

    public async Task<PagedResult<AreaListItemDto>> ListAsync(AreaListQuery query, Guid? parentId, bool directChildrenOnly,
        CancellationToken cancellationToken)
    {
        var source = AreaTreeQueries.ReadTree(_context);
        if (directChildrenOnly) source = source.Where(a => a.ParentId == parentId);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = TextNormalization.Name(query.Search).ToLowerInvariant();
            source = source.Where(a => a.NormalizedName.Contains(search) || a.Code.ToLower().Contains(search));
        }
        if (query.Status is { } status) source = source.Where(a => a.IsLocked == (status == EntityStatus.Locked));
        if (query.EffectiveStatus is { } effective) source = source.Where(a => a.IsEffectivelyLocked == (effective == EntityStatus.Locked));
        var total = await source.CountAsync(cancellationToken);
        var ordered = (query.SortBy, query.SortDirection) switch
        {
            (AreaSortBy.Code, AreaSortDirection.Asc) => source.OrderBy(a => a.Code),
            (AreaSortBy.Code, AreaSortDirection.Desc) => source.OrderByDescending(a => a.Code),
            (AreaSortBy.Name, AreaSortDirection.Desc) => source.OrderByDescending(a => a.Name),
            _ => source.OrderBy(a => a.Name)
        };
        var items = await ordered.ThenBy(a => a.Id).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .ProjectTo<AreaListItemDto>(mapper.ConfigurationProvider).ToListAsync(cancellationToken);
        return new(items, total, query.Page, query.PageSize);
    }

    public Task<AreaDetailDto?> GetDetailAsync(Guid id, CancellationToken cancellationToken)
        => AreaTreeQueries.ReadTree(_context).Where(a => a.Id == id)
            .ProjectTo<AreaDetailDto>(mapper.ConfigurationProvider).SingleOrDefaultAsync(cancellationToken);

    public Task<AreaManagementDto> GetManagementAsync(Guid id, CancellationToken cancellationToken)
        => _dbSet.AsNoTracking().Where(a => a.Id == id).Select(a => new AreaManagementDto
        {
            CreatedBy = _context.AreaHistories.Where(h => h.AreaId == id && h.ActionCode == "AREA_CREATED"
                    && h.Outcome == AuditOutcome.Success).OrderBy(h => h.OccurredAt).ThenBy(h => h.Id)
                .Select(h => h.ActorAccountId).FirstOrDefault(),
            CreatedAt = _context.AreaHistories.Where(h => h.AreaId == id && h.ActionCode == "AREA_CREATED"
                    && h.Outcome == AuditOutcome.Success).OrderBy(h => h.OccurredAt).ThenBy(h => h.Id)
                .Select(h => (DateTimeOffset?)h.OccurredAt).FirstOrDefault()
        }).SingleAsync(cancellationToken);

    public Task<int> CountDescendantsAsync(Guid id, CancellationToken cancellationToken)
        => AreaTreeQueries.SubtreeIds(_context, id).CountAsync(value => value != id, cancellationToken);
}
