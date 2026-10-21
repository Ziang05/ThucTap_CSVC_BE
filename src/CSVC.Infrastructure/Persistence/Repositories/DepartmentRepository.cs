using CSVC.Core.Departments;
using CSVC.UseCases.Abstractions.Persistence;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using CSVC.Core.Common;
using CSVC.UseCases.Common;
using CSVC.UseCases.Departments.DTOs;
using Microsoft.EntityFrameworkCore;

namespace CSVC.Infrastructure.Persistence.Repositories;

public sealed class DepartmentRepository(CsvcDbContext context, IMapper mapper)
    : GenericRepository<Department>(context), IDepartmentRepository
{
    public Task<Department?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
        => FirstOrDefaultAsync(department => department.Code == code, cancellationToken);

    public Task<bool> NameExistsAsync(string name, Guid? exceptId, CancellationToken cancellationToken)
    {
        var key = TextNormalization.Name(name).ToLowerInvariant();
        return _dbSet.AnyAsync(d => d.Id != exceptId && EF.Property<string>(d, "NormalizedName") == key, cancellationToken);
    }

    public async Task<PagedResult<DepartmentListItemDto>> ListAsync(DepartmentListQuery query, CancellationToken cancellationToken)
    {
        var source = _dbSet.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = TextNormalization.Name(query.Search).ToLowerInvariant();
            source = source.Where(d => EF.Property<string>(d, "NormalizedName").Contains(search)
                || (d.ShortName != null && d.ShortName.ToLower().Contains(search)));
        }
        if (query.Status is { } status) source = source.Where(d => d.Status == status);
        var total = await source.CountAsync(cancellationToken);
        var ordered = (query.SortBy, query.SortDirection) switch
        {
            (DepartmentSortBy.Code, DepartmentSortDirection.Asc) => source.OrderBy(d => d.Code),
            (DepartmentSortBy.Code, DepartmentSortDirection.Desc) => source.OrderByDescending(d => d.Code),
            (DepartmentSortBy.Status, DepartmentSortDirection.Asc) => source.OrderBy(d => d.Status),
            (DepartmentSortBy.Status, DepartmentSortDirection.Desc) => source.OrderByDescending(d => d.Status),
            (DepartmentSortBy.Name, DepartmentSortDirection.Desc) => source.OrderByDescending(d => d.Name),
            _ => source.OrderBy(d => d.Name)
        };
        var items = await ordered.ThenBy(d => d.Id).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .ProjectTo<DepartmentListItemDto>(mapper.ConfigurationProvider).ToListAsync(cancellationToken);
        return new(items, total, query.Page, query.PageSize);
    }

    public Task<DepartmentDetailDto?> GetDetailAsync(Guid id, CancellationToken cancellationToken)
        => _dbSet.AsNoTracking().Where(d => d.Id == id).ProjectTo<DepartmentDetailDto>(mapper.ConfigurationProvider)
            .SingleOrDefaultAsync(cancellationToken);

    public Task<DepartmentManagementDto> GetManagementAsync(Guid id, CancellationToken cancellationToken)
        => _dbSet.AsNoTracking().Where(d => d.Id == id).Select(d => new DepartmentManagementDto
        {
            CreatedBy = _context.DepartmentHistories.Where(h => h.DepartmentId == id && h.ActionCode == "DEPARTMENT_CREATED"
                    && h.Outcome == AuditOutcome.Success).OrderBy(h => h.OccurredAt).ThenBy(h => h.Id)
                .Select(h => h.ActorAccountId).FirstOrDefault(),
            CreatedAt = _context.DepartmentHistories.Where(h => h.DepartmentId == id && h.ActionCode == "DEPARTMENT_CREATED"
                    && h.Outcome == AuditOutcome.Success).OrderBy(h => h.OccurredAt).ThenBy(h => h.Id)
                .Select(h => (DateTimeOffset?)h.OccurredAt).FirstOrDefault(),
            AccountCount = (from a in _context.Accounts join p in _context.UserProfiles on a.UserProfileId equals p.Id
                            where p.DepartmentId == id select a.Id).Count()
        }).SingleAsync(cancellationToken);
}
