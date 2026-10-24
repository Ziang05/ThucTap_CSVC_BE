using AutoMapper;
using AutoMapper.QueryableExtensions;
using CSVC.Core.Common;
using CSVC.Core.Manufacturers;
using CSVC.UseCases.Abstractions.Persistence;
using CSVC.UseCases.Common;
using CSVC.UseCases.Manufacturers.DTOs;
using CSVC.UseCases.Manufacturers.Models;
using CSVC.UseCases.History.DTOs;
using Microsoft.EntityFrameworkCore;

namespace CSVC.Infrastructure.Persistence.Repositories;

public sealed class ManufacturerHistoryRepository(CsvcDbContext context, IMapper mapper)
    : GenericRepository<ManufacturerHistory>(context), IManufacturerHistoryRepository
{
    // Extract event-time labels in PostgreSQL so filtering/paging never loads raw audit JSON.
    private const string Labels = """
        h.id AS "Id", h.manufacturer_id AS "ManufacturerId", h.action_code AS "ActionCode",
        h.outcome AS "Outcome", h.outcome::text AS "OutcomeCode", h.occurred_at AS "OccurredAt",
        a.account_code AS "ActorAccountCode", p.full_name AS "ActorFullName",
        COALESCE(h.after_data->>'display_code', h.before_data->>'display_code') AS "DisplayCode",
        COALESCE(h.after_data->>'name', h.before_data->>'name') AS "Name"
        """;
    private const string Source = """
        FROM manufacturer_history AS h
        LEFT JOIN accounts AS a ON a.id = h.actor_account_id
        LEFT JOIN user_profiles AS p ON p.id = a.user_profile_id
        """;

    public async Task<PagedResult<ManufacturerHistoryListItemDto>> ListAsync(ManufacturerHistoryQuery query, CancellationToken ct, Guid? scope = null)
    {
        var source = _context.Database.SqlQueryRaw<ManufacturerHistoryReadModel>("SELECT " + Labels + "\n" + Source);
        if (scope is { } manufacturerId) source = source.Where(h => h.ManufacturerId == manufacturerId);
        if (query.From is { } from) source = source.Where(h => h.OccurredAt >= from);
        if (query.To is { } to) source = source.Where(h => h.OccurredAt <= to);
        if (query.Outcome is { } outcome) source = source.Where(h => h.Outcome == outcome);
        if (!string.IsNullOrWhiteSpace(query.DisplayCode))
        {
            var code = query.DisplayCode.Trim().ToLowerInvariant();
            source = source.Where(h => h.DisplayCode != null && h.DisplayCode.ToLower() == code);
        }
        if (!string.IsNullOrWhiteSpace(query.ActorAccountCode))
        {
            var code = query.ActorAccountCode.Trim().ToLowerInvariant();
            source = source.Where(h => h.ActorAccountCode != null && h.ActorAccountCode.ToLower() == code);
        }
        if (!string.IsNullOrWhiteSpace(query.ActionCode))
        {
            var action = query.ActionCode.Trim().ToUpperInvariant();
            source = source.Where(h => h.ActionCode == action);
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = TextNormalization.Name(query.Search).ToLowerInvariant();
            source = source.Where(h => h.DisplayCode != null && h.DisplayCode.ToLower().Contains(search)
                || h.Name != null && h.Name.ToLower().Contains(search));
        }
        var total = await source.CountAsync(ct);
        var ordered = (query.SortBy, query.SortDirection) switch
        {
            (HistorySortBy.ActionCode, HistorySortDirection.Asc) => source.OrderBy(h => h.ActionCode),
            (HistorySortBy.ActionCode, HistorySortDirection.Desc) => source.OrderByDescending(h => h.ActionCode),
            (HistorySortBy.Outcome, HistorySortDirection.Asc) => source.OrderBy(h => h.OutcomeCode),
            (HistorySortBy.Outcome, HistorySortDirection.Desc) => source.OrderByDescending(h => h.OutcomeCode),
            (HistorySortBy.ActorAccountCode, HistorySortDirection.Asc) => source.OrderBy(h => h.ActorAccountCode),
            (HistorySortBy.ActorAccountCode, HistorySortDirection.Desc) => source.OrderByDescending(h => h.ActorAccountCode),
            (_, HistorySortDirection.Asc) => source.OrderBy(h => h.OccurredAt),
            _ => source.OrderByDescending(h => h.OccurredAt)
        };
        var items = await ordered.ThenBy(h => h.Id).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .ProjectTo<ManufacturerHistoryListItemDto>(mapper.ConfigurationProvider).ToListAsync(ct);
        return new(items, total, query.Page, query.PageSize);
    }

    public Task<ManufacturerHistoryDetailReadModel?> GetDetailAsync(Guid id, CancellationToken ct, Guid? scope = null)
    {
        var source = _context.Database.SqlQueryRaw<ManufacturerHistoryDetailReadModel>("SELECT " + Labels + """
            , h.request_id AS "RequestId", h.reason_code AS "ReasonCode", h.changed_fields AS "ChangedFields",
            h.before_data AS "BeforeData", h.after_data AS "AfterData"
            """ + "\n" + Source).Where(h => h.Id == id);
        if (scope is { } manufacturerId) source = source.Where(h => h.ManufacturerId == manufacturerId);
        return source.SingleOrDefaultAsync(ct);
    }
}
