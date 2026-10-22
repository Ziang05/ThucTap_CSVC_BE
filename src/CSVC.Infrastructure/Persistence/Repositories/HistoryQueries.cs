using AutoMapper;
using AutoMapper.QueryableExtensions;
using CSVC.Core.Common;
using CSVC.UseCases.Common;
using CSVC.UseCases.History.DTOs;
using CSVC.UseCases.History.Models;
using Microsoft.EntityFrameworkCore;

namespace CSVC.Infrastructure.Persistence.Repositories;

internal static class HistoryQueries
{
    internal sealed class AccountLabel
    {
        public Guid Id { get; init; }
        public string Code { get; init; } = "";
        public string Name { get; init; } = "";
    }

    public static IQueryable<AccountLabel> Accounts(CsvcDbContext context)
        => from account in context.Accounts.AsNoTracking()
           join profile in context.UserProfiles.AsNoTracking() on account.UserProfileId equals profile.Id
           select new AccountLabel { Id = account.Id, Code = account.AccountCode, Name = profile.FullName };

    public static async Task<PagedResult<TDto>> PageAsync<TRead, TDto>(IQueryable<TRead> source,
        HistoryQuery query, IMapper mapper, CancellationToken ct) where TRead : HistoryReadModel
    {
        if (query.From is { } from) source = source.Where(h => h.OccurredAt >= from);
        if (query.To is { } to) source = source.Where(h => h.OccurredAt <= to);
        if (query.ActorAccountId is { } actor) source = source.Where(h => h.ActorAccountId == actor);
        if (query.RequestId is { } request) source = source.Where(h => h.RequestId == request);
        if (query.Outcome is { } outcome) source = source.Where(h => h.Outcome == outcome);
        if (!string.IsNullOrWhiteSpace(query.ActionCode))
        {
            var action = query.ActionCode.Trim().ToUpperInvariant();
            source = source.Where(h => h.ActionCode.ToUpper() == action);
        }
        if (!string.IsNullOrWhiteSpace(query.ReasonCode))
        {
            var reason = query.ReasonCode.Trim().ToUpperInvariant();
            source = source.Where(h => h.ReasonCode != null && h.ReasonCode.ToUpper() == reason);
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = TextNormalization.Name(query.Search).ToLowerInvariant();
            source = source.Where(h => h.SearchText.ToLower().Contains(search));
        }
        var count = await source.CountAsync(ct);
        var ordered = (query.SortBy, query.SortDirection) switch
        {
            (HistorySortBy.ActionCode, HistorySortDirection.Asc) => source.OrderBy(h => h.ActionCode),
            (HistorySortBy.ActionCode, HistorySortDirection.Desc) => source.OrderByDescending(h => h.ActionCode),
            (HistorySortBy.Outcome, HistorySortDirection.Asc) => source.OrderBy(h => h.Outcome),
            (HistorySortBy.Outcome, HistorySortDirection.Desc) => source.OrderByDescending(h => h.Outcome),
            (HistorySortBy.ActorAccountCode, HistorySortDirection.Asc) => source.OrderBy(h => h.ActorAccountCode),
            (HistorySortBy.ActorAccountCode, HistorySortDirection.Desc) => source.OrderByDescending(h => h.ActorAccountCode),
            (_, HistorySortDirection.Asc) => source.OrderBy(h => h.OccurredAt),
            _ => source.OrderByDescending(h => h.OccurredAt)
        };
        var items = await ordered.ThenBy(h => h.Id).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .ProjectTo<TDto>(mapper.ConfigurationProvider).ToListAsync(ct);
        return new(items, count, query.Page, query.PageSize);
    }
}
