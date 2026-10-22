using CSVC.Core.Accounts;
using CSVC.UseCases.Abstractions.Persistence;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using CSVC.UseCases.Common;
using CSVC.UseCases.History.DTOs;
using CSVC.UseCases.History.Models;
using Microsoft.EntityFrameworkCore;

namespace CSVC.Infrastructure.Persistence.Repositories;

public sealed class AccountHistoryRepository(CsvcDbContext context, IMapper mapper)
    : GenericRepository<AccountHistory>(context), IAccountHistoryRepository
{
    public Task<PagedResult<AccountHistoryListItemDto>> ListAsync(AccountHistoryQuery query, CancellationToken ct)
    {
        var source = Read();
        if (query.TargetAccountId is { } target) source = source.Where(h => h.TargetAccountId == target);
        return HistoryQueries.PageAsync<AccountHistoryReadModel, AccountHistoryListItemDto>(source, query, mapper, ct);
    }

    public Task<AccountHistoryDetailDto?> GetDetailAsync(Guid id, CancellationToken ct)
        => Read().Where(h => h.Id == id).ProjectTo<AccountHistoryDetailDto>(mapper.ConfigurationProvider).SingleOrDefaultAsync(ct);

    private IQueryable<AccountHistoryReadModel> Read()
        => from history in _dbSet.AsNoTracking()
           join actor in HistoryQueries.Accounts(_context) on history.ActorAccountId equals actor.Id into actors
           from actor in actors.DefaultIfEmpty()
           join target in HistoryQueries.Accounts(_context) on history.TargetAccountId equals target.Id into targets
           from target in targets.DefaultIfEmpty()
           select new AccountHistoryReadModel
           {
               Id = history.Id, RequestId = history.RequestId, OccurredAt = history.OccurredAt,
               ActionCode = history.ActionCode, Outcome = history.Outcome, ReasonCode = history.ReasonCode,
               ActorAccountId = history.ActorAccountId, ActorAccountCode = actor.Code, ActorFullName = actor.Name,
               TargetAccountId = history.TargetAccountId, TargetAccountCode = target.Code, TargetFullName = target.Name,
               ChangedFields = history.ChangedFields, BeforeData = history.BeforeData, AfterData = history.AfterData,
               SearchText = history.ActionCode + " " + (history.ReasonCode ?? "") + " " + (actor.Code ?? "") + " "
                   + (actor.Name ?? "") + " " + (target.Code ?? "") + " " + (target.Name ?? "")
           };
}
