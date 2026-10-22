using CSVC.Core.Areas;
using CSVC.UseCases.Abstractions.Persistence;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using CSVC.UseCases.Areas.Models;
using CSVC.UseCases.Common;
using CSVC.UseCases.History.DTOs;
using Microsoft.EntityFrameworkCore;

namespace CSVC.Infrastructure.Persistence.Repositories;

public sealed class AreaHistoryRepository(CsvcDbContext context, IMapper mapper)
    : GenericRepository<AreaHistory>(context), IAreaHistoryRepository
{
    public Task<PagedResult<AreaHistoryListItemDto>> ListAsync(AreaHistoryQuery query, CancellationToken ct, Guid? areaScope = null)
    {
        var source = Read();
        if (areaScope is { } scope) source = FilterArea(source, scope, query.IncludeDescendants);
        if (query.AreaId is { } area) source = FilterArea(source, area, query.IncludeDescendants);
        return HistoryQueries.PageAsync<AreaHistoryReadModel, AreaHistoryListItemDto>(source, query, mapper, ct);
    }

    public Task<AreaHistoryDetailDto?> GetDetailAsync(Guid id, CancellationToken ct, Guid? areaScope = null, bool includeDescendants = false)
    {
        var source = Read().Where(h => h.Id == id);
        if (areaScope is { } scope) source = FilterArea(source, scope, includeDescendants);
        return source.ProjectTo<AreaHistoryDetailDto>(mapper.ConfigurationProvider).SingleOrDefaultAsync(ct);
    }

    private IQueryable<AreaHistoryReadModel> FilterArea(IQueryable<AreaHistoryReadModel> source, Guid id, bool includeDescendants)
    {
        if (!includeDescendants) return source.Where(h => h.AreaId == id);
        var ids = AreaTreeQueries.SubtreeIds(_context, id);
        return source.Where(h => h.AreaId.HasValue && ids.Contains(h.AreaId.Value));
    }

    private IQueryable<AreaHistoryReadModel> Read()
        => from history in _dbSet.AsNoTracking()
           join actor in HistoryQueries.Accounts(_context) on history.ActorAccountId equals actor.Id into actors
           from actor in actors.DefaultIfEmpty()
           join area in _context.Areas.AsNoTracking() on history.AreaId equals area.Id into areas
           from area in areas.DefaultIfEmpty()
           select new AreaHistoryReadModel
           {
               Id = history.Id, RequestId = history.RequestId, OccurredAt = history.OccurredAt,
               ActorAccountId = history.ActorAccountId, ActorAccountCode = actor.Code, ActorFullName = actor.Name,
               AreaId = history.AreaId, AreaCode = area.Code, AreaName = area.Name,
               ActionCode = history.ActionCode, Outcome = history.Outcome, ReasonCode = history.ReasonCode,
               ChangedFields = history.ChangedFields, BeforeData = history.BeforeData, AfterData = history.AfterData,
               SearchText = history.ActionCode + " " + (history.ReasonCode ?? "") + " " + (actor.Code ?? "") + " "
                   + (actor.Name ?? "") + " " + (area.Code ?? "") + " " + (area.Name ?? "")
           };
}
