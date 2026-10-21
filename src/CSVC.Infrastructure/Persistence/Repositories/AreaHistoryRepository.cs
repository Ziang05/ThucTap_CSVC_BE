using CSVC.Core.Areas;
using CSVC.UseCases.Abstractions.Persistence;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using CSVC.UseCases.Areas.DTOs;
using CSVC.UseCases.Common;
using Microsoft.EntityFrameworkCore;

namespace CSVC.Infrastructure.Persistence.Repositories;

public sealed class AreaHistoryRepository(CsvcDbContext context, IMapper mapper)
    : GenericRepository<AreaHistory>(context), IAreaHistoryRepository
{
    public async Task<PagedResult<AreaHistoryDto>> ListAsync(Guid id, AreaHistoryQuery query, CancellationToken cancellationToken)
    {
        var source = _dbSet.AsNoTracking();
        if (query.IncludeDescendants)
        {
            var ids = AreaTreeQueries.SubtreeIds(_context, id);
            source = source.Where(h => h.AreaId.HasValue && ids.Contains(h.AreaId.Value));
        }
        else source = source.Where(h => h.AreaId == id);
        var total = await source.CountAsync(cancellationToken);
        var items = await source.OrderByDescending(h => h.OccurredAt).ThenBy(h => h.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .ProjectTo<AreaHistoryDto>(mapper.ConfigurationProvider).ToListAsync(cancellationToken);
        return new(items, total, query.Page, query.PageSize);
    }
}
