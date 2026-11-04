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
        var query = _context.ServiceOrders.Include(x => x.Items).ThenInclude(x => x.RepairDetail)!.ThenInclude(x => x!.DamageReport)
            .Include(x => x.ExportSlips).ThenInclude(x => x.Items).ThenInclude(x => x.ReturnItem)!.ThenInclude(x => x!.ReturnSlip).AsSplitQuery();
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

    public async Task<PagedResult<ExportSlip>> ListExportsAsync(ExportSlipListQuery query, CancellationToken ct)
    {
        var source = _context.ExportSlips.AsNoTracking().AsQueryable();
        if (query.Status is { } status) source = source.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(query.ServiceOrderCode))
        {
            var code = query.ServiceOrderCode.Trim().ToUpperInvariant();
            source = source.Where(x => _context.ServiceOrders.Any(o => o.Id == x.ServiceOrderId && o.DisplayCode == code));
        }
        var total = await source.CountAsync(ct);
        var items = await source.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.DisplayCode)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new(items, total, query.Page, query.PageSize);
    }

    public async Task<IReadOnlyList<ServiceOrder>> GetByExportIdsAsync(IEnumerable<Guid> ids, CancellationToken ct)
        => await Aggregate(false).Where(o => o.ExportSlips.Any(e => ids.Contains(e.Id))).ToListAsync(ct);
    public async Task<int> NextReturnSequenceAsync(Guid exportId, CancellationToken ct)
        => (await _context.Set<ExportReturnSlip>().Where(r => r.ExportSlipId == exportId).Select(r => (int?)r.SequenceNo).MaxAsync(ct) ?? 0) + 1;
    public Task<ExportReturnSlip?> GetReturnAsync(string code, CancellationToken ct)
        => _context.Set<ExportReturnSlip>().AsNoTracking().Include(x => x.Items).SingleOrDefaultAsync(x => x.DisplayCode == code, ct);
    public Task<ExportReturnSlip?> GetReturnByRequestAsync(Guid requestId, CancellationToken ct)
        => _context.Set<ExportReturnSlip>().AsNoTracking().Include(x => x.Items).SingleOrDefaultAsync(x => x.CreateRequestId == requestId, ct);
    public async Task<PagedResult<ExportReturnSlip>> ListReturnsAsync(ExportReturnListQuery query, CancellationToken ct)
    {
        var source = _context.Set<ExportReturnSlip>().AsNoTracking().Include(x => x.Items).AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.ExportSlipCode))
        {
            var code = query.ExportSlipCode.Trim().ToUpperInvariant();
            source = source.Where(x => _context.ExportSlips.Any(e => e.Id == x.ExportSlipId && e.DisplayCode == code));
        }
        var total = await source.CountAsync(ct);
        var items = await source.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.DisplayCode)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new(items, total, query.Page, query.PageSize);
    }
    public async Task<PagedResult<ServiceHistoryDto>> ReturnHistoryAsync(Guid returnId, ServiceHistoryQuery query, CancellationToken ct)
    {
        var source = _context.Set<ExportReturnHistory>().AsNoTracking().Where(x => x.ReturnSlipId == returnId);
        var total = await source.CountAsync(ct);
        var items = await source.OrderBy(x => x.OccurredAt).ThenBy(x => x.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(x => new ServiceHistoryDto(x.ActionCode, x.Outcome.ToString(), x.OccurredAt,
                _context.Accounts.Where(a => a.Id == x.ActorAccountId).Select(a => a.AccountCode).FirstOrDefault(),
                (from ri in _context.Set<ExportReturnSlipItem>()
                 join ei in _context.ExportSlipItems on ri.ExportSlipItemId equals ei.Id
                 join si in _context.ServiceOrderItems on ei.ServiceOrderItemId equals si.Id
                 where ri.Id == x.ReturnSlipItemId
                 select (int?)si.ItemNo).FirstOrDefault(),
                null, null, x.BeforeData, x.AfterData)).ToListAsync(ct);
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
                (from ei in _context.ExportSlipItems
                 join si in _context.ServiceOrderItems on ei.ServiceOrderItemId equals si.Id
                 where ei.Id == x.ExportSlipItemId
                 select (int?)si.ItemNo).FirstOrDefault(),
                null, x.ReasonCode, x.BeforeData, x.AfterData)).ToListAsync(ct);
        return new(items, total, query.Page, query.PageSize);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> AccountCodesAsync(IEnumerable<Guid> ids, CancellationToken ct)
        => await _context.Accounts.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.AccountCode, ct);
}
