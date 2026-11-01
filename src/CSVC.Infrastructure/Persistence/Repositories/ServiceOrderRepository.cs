using CSVC.Core.ServiceOrders;
using CSVC.UseCases.Abstractions.Persistence;
using CSVC.UseCases.Common;
using CSVC.UseCases.ServiceOrders.DTOs;
using Microsoft.EntityFrameworkCore;

namespace CSVC.Infrastructure.Persistence.Repositories;

public sealed class ServiceOrderRepository(CsvcDbContext context) : GenericRepository<ServiceOrder>(context), IServiceOrderRepository
{
    private IQueryable<ServiceOrder> Aggregate(bool tracked)
    {
        var query = _context.ServiceOrders.Include(x => x.Items).ThenInclude(x => x.RepairDetail)
            .Include(x => x.ExportSlips).ThenInclude(x => x.Items).AsSplitQuery();
        return tracked ? query : query.AsNoTracking();
    }

    public Task<ServiceOrder?> GetAggregateAsync(string code, bool tracked, CancellationToken ct)
        => Aggregate(tracked).SingleOrDefaultAsync(x => x.DisplayCode == code, ct);
    public Task<ServiceOrder?> GetByCreateRequestAsync(Guid requestId, CancellationToken ct)
        => Aggregate(false).SingleOrDefaultAsync(x => x.CreateRequestId == requestId, ct);
    public Task<ServiceOrder?> GetByExportCodeAsync(string code, bool tracked, CancellationToken ct)
        => Aggregate(tracked).SingleOrDefaultAsync(x => x.ExportSlips.Any(e => e.DisplayCode == code), ct);

    public async Task<PagedResult<ServiceOrder>> ListAsync(ServiceOrderListQuery query, CancellationToken ct)
    {
        var source = Aggregate(false);
        if (query.OrderType is { } type) source = source.Where(x => x.OrderType == type);
        if (query.Status is { } status) source = source.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToUpperInvariant();
            source = source.Where(x => x.DisplayCode.Contains(search));
        }
        var total = await source.CountAsync(ct);
        var items = await source.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.DisplayCode)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new(items, total, query.Page, query.PageSize);
    }

    public async Task<PagedResult<ExportSlipDto>> ListExportsAsync(ExportSlipListQuery query, CancellationToken ct)
    {
        var source = _context.ExportSlips.AsNoTracking().AsQueryable();
        if (query.Status is { } status) source = source.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(query.ServiceOrderCode))
        {
            var code = query.ServiceOrderCode.Trim().ToUpperInvariant();
            source = source.Where(x => _context.ServiceOrders.Any(o => o.Id == x.ServiceOrderId && o.DisplayCode == code));
        }
        var total = await source.CountAsync(ct);
        var items = await (from slip in source
            join order in _context.ServiceOrders on slip.ServiceOrderId equals order.Id
            join creator in _context.Accounts on slip.CreatedByAccountId equals creator.Id
            orderby slip.CreatedAt descending, slip.DisplayCode
            select new ExportSlipDto(slip.DisplayCode, order.DisplayCode, order.OrderType, slip.SequenceNo, slip.Status,
                slip.FacilityNameSnapshot, slip.FacilityAddressSnapshot, slip.FacilityContactSnapshot,
                slip.PlannedExportAt, slip.Notes, creator.AccountCode, slip.CreatedAt, slip.Result,
                _context.Accounts.Where(a => a.Id == slip.ResultRecordedByAccountId).Select(a => a.AccountCode).FirstOrDefault(),
                slip.ResultRecordedAt, slip.RecipientName, slip.RecipientContact, slip.FailureReason, slip.EvidenceRef,
                slip.CanceledAt, slip.CompletedAt, slip.Version,
                (from line in _context.ExportSlipItems
                 join item in _context.ServiceOrderItems on line.ServiceOrderItemId equals item.Id
                 where line.ExportSlipId == slip.Id
                 orderby item.ItemNo
                 select new ExportSlipItemDto(item.ItemNo, line.DeviceCodeSnapshot, line.DeviceNameSnapshot, line.SerialSnapshot)).ToArray()))
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new(items, total, query.Page, query.PageSize);
    }

    public async Task<PagedResult<ServiceHistoryDto>> HistoryAsync(Guid orderId, ServiceHistoryQuery query, CancellationToken ct)
    {
        var source = _context.ServiceOrderHistories.AsNoTracking().Where(x => x.ServiceOrderId == orderId);
        var total = await source.CountAsync(ct);
        var items = await source.OrderBy(x => x.OccurredAt).ThenBy(x => x.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(x => new ServiceHistoryDto(x.ActionCode, x.Outcome.ToString(), x.OccurredAt,
                _context.Accounts.Where(a => a.Id == x.ActorAccountId).Select(a => a.AccountCode).FirstOrDefault(),
                _context.ServiceOrderItems.Where(i => i.Id == x.ServiceOrderItemId).Select(i => (int?)i.ItemNo).FirstOrDefault(),
                _context.ExportSlips.Where(e => e.Id == x.RelatedExportSlipId).Select(e => e.DisplayCode).FirstOrDefault(),
                x.ReasonCode, x.BeforeData, x.AfterData)).ToListAsync(ct);
        return new(items, total, query.Page, query.PageSize);
    }

    public async Task<PagedResult<ServiceHistoryDto>> ExportHistoryAsync(Guid slipId, ServiceHistoryQuery query, CancellationToken ct)
    {
        var source = _context.ExportHistories.AsNoTracking().Where(x => x.ExportSlipId == slipId);
        var total = await source.CountAsync(ct);
        var items = await source.OrderBy(x => x.OccurredAt).ThenBy(x => x.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(x => new ServiceHistoryDto(x.ActionCode, x.Outcome.ToString(), x.OccurredAt,
                _context.Accounts.Where(a => a.Id == x.ActorAccountId).Select(a => a.AccountCode).FirstOrDefault(),
                null, null, x.ReasonCode, x.BeforeData, x.AfterData)).ToListAsync(ct);
        return new(items, total, query.Page, query.PageSize);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> AccountCodesAsync(IEnumerable<Guid> ids, CancellationToken ct)
        => await _context.Accounts.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.AccountCode, ct);
}
