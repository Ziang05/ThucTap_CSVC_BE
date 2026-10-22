using CSVC.Core.Authentication;
using CSVC.UseCases.Abstractions.Persistence;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using CSVC.UseCases.Common;
using CSVC.UseCases.History.DTOs;
using CSVC.UseCases.History.Models;
using Microsoft.EntityFrameworkCore;

namespace CSVC.Infrastructure.Persistence.Repositories;

public sealed class AuthHistoryRepository(CsvcDbContext context, IMapper mapper)
    : GenericRepository<AuthHistory>(context), IAuthHistoryRepository
{
    public Task<PagedResult<AuthHistoryListItemDto>> ListAsync(AuthHistoryQuery query, CancellationToken ct)
    {
        var source = Read();
        if (query.AccountId is { } account) source = source.Where(h => h.AccountId == account);
        if (query.SessionId is { } session) source = source.Where(h => h.SessionId == session);
        if (!string.IsNullOrWhiteSpace(query.EmailSubmitted))
        {
            var email = query.EmailSubmitted.Trim().ToLowerInvariant();
            source = source.Where(h => h.EmailSubmitted != null && h.EmailSubmitted.ToLower() == email);
        }
        return HistoryQueries.PageAsync<AuthHistoryReadModel, AuthHistoryListItemDto>(source, query, mapper, ct);
    }

    public Task<AuthHistoryDetailDto?> GetDetailAsync(Guid id, CancellationToken ct)
        => Read().Where(h => h.Id == id).ProjectTo<AuthHistoryDetailDto>(mapper.ConfigurationProvider).SingleOrDefaultAsync(ct);

    private IQueryable<AuthHistoryReadModel> Read()
        => from history in _dbSet.AsNoTracking()
           join actor in HistoryQueries.Accounts(_context) on history.ActorAccountId equals actor.Id into actors
           from actor in actors.DefaultIfEmpty()
           join account in HistoryQueries.Accounts(_context) on history.AccountId equals account.Id into accounts
           from account in accounts.DefaultIfEmpty()
           select new AuthHistoryReadModel
           {
               Id = history.Id, RequestId = history.RequestId, OccurredAt = history.OccurredAt,
               ActionCode = history.ActionCode, Outcome = history.Outcome, ReasonCode = history.ReasonCode,
               ActorAccountId = history.ActorAccountId, ActorAccountCode = actor.Code, ActorFullName = actor.Name,
               AccountId = history.AccountId, AccountCode = account.Code, FullName = account.Name,
               SessionId = history.SessionId, EmailSubmitted = history.EmailSubmitted, BlockedUntil = history.BlockedUntil,
               IpAddress = history.IpAddress, UserAgent = history.UserAgent,
               SearchText = history.ActionCode + " " + (history.ReasonCode ?? "") + " " + (actor.Code ?? "") + " "
                   + (actor.Name ?? "") + " " + (account.Code ?? "") + " " + (account.Name ?? "") + " " + (history.EmailSubmitted ?? "")
           };
}
