using CSVC.Core.Departments;
using CSVC.UseCases.Abstractions.Persistence;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using CSVC.UseCases.Common;
using CSVC.UseCases.History.DTOs;
using CSVC.UseCases.History.Models;
using Microsoft.EntityFrameworkCore;

namespace CSVC.Infrastructure.Persistence.Repositories;

public sealed class DepartmentHistoryRepository(CsvcDbContext context, IMapper mapper)
    : GenericRepository<DepartmentHistory>(context), IDepartmentHistoryRepository
{
    public Task<PagedResult<DepartmentHistoryListItemDto>> ListAsync(DepartmentHistoryQuery query, CancellationToken ct, Guid? departmentScope = null)
    {
        var source = Read();
        if (departmentScope is { } scope) source = source.Where(h => h.DepartmentId == scope);
        if (query.DepartmentId is { } department) source = source.Where(h => h.DepartmentId == department);
        return HistoryQueries.PageAsync<DepartmentHistoryReadModel, DepartmentHistoryListItemDto>(source, query, mapper, ct);
    }

    public Task<DepartmentHistoryDetailDto?> GetDetailAsync(Guid id, CancellationToken ct, Guid? departmentScope = null)
    {
        var source = Read().Where(h => h.Id == id);
        if (departmentScope is { } scope) source = source.Where(h => h.DepartmentId == scope);
        return source.ProjectTo<DepartmentHistoryDetailDto>(mapper.ConfigurationProvider).SingleOrDefaultAsync(ct);
    }

    private IQueryable<DepartmentHistoryReadModel> Read()
        => from history in _dbSet.AsNoTracking()
           join actor in HistoryQueries.Accounts(_context) on history.ActorAccountId equals actor.Id into actors
           from actor in actors.DefaultIfEmpty()
           join department in _context.Departments.AsNoTracking() on history.DepartmentId equals department.Id into departments
           from department in departments.DefaultIfEmpty()
           select new DepartmentHistoryReadModel
           {
               Id = history.Id, RequestId = history.RequestId, OccurredAt = history.OccurredAt,
               ActionCode = history.ActionCode, Outcome = history.Outcome, ReasonCode = history.ReasonCode,
               ActorAccountId = history.ActorAccountId, ActorAccountCode = actor.Code, ActorFullName = actor.Name,
               DepartmentId = history.DepartmentId, DepartmentCode = department.Code, DepartmentName = department.Name,
               BeforeData = history.BeforeData, AfterData = history.AfterData,
               SearchText = history.ActionCode + " " + (history.ReasonCode ?? "") + " " + (actor.Code ?? "") + " "
                   + (actor.Name ?? "") + " " + (department.Code ?? "") + " " + (department.Name ?? "")
           };
}
