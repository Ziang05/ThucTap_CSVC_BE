using CSVC.Core.Departments;
using CSVC.UseCases.Abstractions.Persistence;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using CSVC.UseCases.Common;
using CSVC.UseCases.Departments.DTOs;
using Microsoft.EntityFrameworkCore;

namespace CSVC.Infrastructure.Persistence.Repositories;

public sealed class DepartmentHistoryRepository(CsvcDbContext context, IMapper mapper)
    : GenericRepository<DepartmentHistory>(context), IDepartmentHistoryRepository
{
    public async Task<PagedResult<DepartmentHistoryDto>> ListAsync(Guid departmentId, DepartmentHistoryQuery query, CancellationToken cancellationToken)
    {
        var source = _dbSet.AsNoTracking().Where(h => h.DepartmentId == departmentId);
        var total = await source.CountAsync(cancellationToken);
        var items = await source.OrderByDescending(h => h.OccurredAt).ThenBy(h => h.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .ProjectTo<DepartmentHistoryDto>(mapper.ConfigurationProvider).ToListAsync(cancellationToken);
        return new(items, total, query.Page, query.PageSize);
    }
}
