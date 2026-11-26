using AutoMapper;
using CSVC.UseCases.Common;
using CSVC.UseCases.DeviceCategories.DTOs;
using CSVC.UseCases.DeviceCategories;
using CSVC.Core.DeviceCategories;
using CSVC.Core.Common;
using CSVC.UseCases.Abstractions.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CSVC.Infrastructure.Persistence.Repositories;

public sealed class DeviceCategoryRepository(CsvcDbContext context, IMapper mapper)
    : GenericRepository<DeviceCategory>(context), IDeviceCategoryRepository
{
    internal static IQueryable<Guid> SubtreeIds(CsvcDbContext context, string normalizedCode)
        => context.Database.SqlQuery<Guid>($"""
            WITH RECURSIVE branch AS (
                SELECT id FROM device_categories WHERE normalized_display_code = {normalizedCode}
                UNION
                SELECT child.id FROM device_categories child JOIN branch ON child.parent_id = branch.id
            )
            SELECT id AS "Value" FROM branch
            """);

    public async Task<PagedResult<CategoryListItemDto>> ListAsync(CategoryListQuery query, CancellationToken ct)
    {
        var source = _dbSet.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = TextNormalization.Name(query.Search).ToLowerInvariant();
            source = source.Where(c => EF.Property<string>(c, "NormalizedName").Contains(search)
                || EF.Property<string>(c, "NormalizedDisplayCode").Contains(search));
        }
        if (query.Status is { } status) source = source.Where(c => c.Status == status);
        var total = await source.CountAsync(ct);
        var ordered = (query.SortBy, query.SortDirection) switch
        {
            (CategorySortBy.DisplayCode, CategorySortDirection.Asc) => source.OrderBy(c => c.DisplayCode),
            (CategorySortBy.DisplayCode, CategorySortDirection.Desc) => source.OrderByDescending(c => c.DisplayCode),
            (CategorySortBy.Status, CategorySortDirection.Asc) => source.OrderBy(c => c.Status),
            (CategorySortBy.Status, CategorySortDirection.Desc) => source.OrderByDescending(c => c.Status),
            (CategorySortBy.Name, CategorySortDirection.Desc) => source.OrderByDescending(c => c.Name),
            _ => source.OrderBy(c => c.Name)
        };
        var page = await ordered.ThenBy(c => c.Id).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .ToListAsync(ct);
        if (page.Count == 0) return new([], total, query.Page, query.PageSize);
        var categories = (await GetAllAsync(ct)).ToDictionary(category => category.Id);
        var items = page.Select(category =>
        {
            var item = mapper.Map<CategoryListItemDto>(category);
            CategoryHierarchy.Populate(item, category, categories);
            return item;
        }).ToArray();
        return new(items, total, query.Page, query.PageSize);
    }

    public async Task<CategoryManagementDto> GetManagementAsync(Guid id, CancellationToken ct)
    {
        var events = from history in _context.CategoryHistories.AsNoTracking()
                     join actor in HistoryQueries.Accounts(_context) on history.ActorAccountId equals actor.Id into actors
                     from actor in actors.DefaultIfEmpty()
                     where history.CategoryId == id && history.Outcome == AuditOutcome.Success
                         && (history.ActionCode == "CATEGORY_CREATED" || history.ActionCode == "CATEGORY_UPDATED"
                             || history.ActionCode == "CATEGORY_LOCKED" || history.ActionCode == "CATEGORY_UNLOCKED")
                     select new { history.Id, history.ActionCode, history.OccurredAt, actor.Code, actor.Name };
        var created = await events.Where(h => h.ActionCode == "CATEGORY_CREATED").OrderBy(h => h.OccurredAt).ThenBy(h => h.Id).FirstOrDefaultAsync(ct);
        var updated = await events.OrderByDescending(h => h.OccurredAt).ThenByDescending(h => h.Id).FirstOrDefaultAsync(ct);
        return new()
        {
            CreatedAt = created?.OccurredAt, CreatedByAccountCode = created?.Code, CreatedByFullName = created?.Name,
            UpdatedAt = updated?.OccurredAt, UpdatedByAccountCode = updated?.Code, UpdatedByFullName = updated?.Name
        };
    }

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
