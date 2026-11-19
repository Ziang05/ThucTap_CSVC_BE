using CSVC.Core.Common;
using CSVC.Core.Materials;
using CSVC.UseCases.Common;
using CSVC.UseCases.Materials;
using Microsoft.EntityFrameworkCore;

namespace CSVC.Infrastructure.Persistence.Repositories;

public sealed class MaterialStore(CsvcDbContext db) : IMaterialStore
{
    private static IQueryable<T> Page<T>(IQueryable<T> source, MaterialQuery q) => source.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize);
    public async Task<PagedResult<UnitDto>> UnitsAsync(MaterialQuery q, CancellationToken ct)
    {
        var source = db.Set<MaterialUnit>().AsNoTracking();
        if (q.Status is { } status) source = source.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(q.Search)) { var search = q.Search.Trim().ToLower(); source = source.Where(x => x.Code.ToLower().Contains(search) || x.Name.ToLower().Contains(search)); }
        var count = await source.CountAsync(ct);
        return new(await Page(source.OrderBy(x => x.Name).ThenBy(x => x.Code), q).Select(x => new UnitDto(x.Code, x.Name, x.DecimalPlaces, x.Status, x.Version)).ToArrayAsync(ct), count, q.Page, q.PageSize);
    }
    public async Task<PagedResult<MaterialDto>> MaterialsAsync(MaterialQuery q, CancellationToken ct)
    {
        var source = db.Set<MaterialItem>().AsNoTracking();
        if (q.Status is { } status) source = source.Where(x => x.Status == status);
        if (q.MaterialKind is { } kind) source = source.Where(x => x.Kind == kind);
        if (!string.IsNullOrWhiteSpace(q.Search)) { var search = q.Search.Trim().ToLower(); source = source.Where(x => x.Code.ToLower().Contains(search) || x.Name.ToLower().Contains(search)); }
        var count = await source.CountAsync(ct);
        var items = await (from x in Page(source.OrderBy(x => x.Name).ThenBy(x => x.Code), q) join unit in db.Set<MaterialUnit>() on x.UnitId equals unit.Id
            select new MaterialDto(x.Code, x.Name, x.Kind, unit.Code, unit.Name, unit.DecimalPlaces, x.Status, x.Notes, x.Version)).ToArrayAsync(ct);
        return new(items, count, q.Page, q.PageSize);
    }
    public async Task<PagedResult<WarehouseDto>> WarehousesAsync(MaterialQuery q, Guid departmentId, CancellationToken ct)
    {
        var source = db.Set<MaterialWarehouse>().AsNoTracking().Where(x => x.DepartmentId == departmentId);
        if (q.Status is { } status) source = source.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(q.Search)) { var search = q.Search.Trim().ToLower(); source = source.Where(x => x.Code.ToLower().Contains(search) || x.Name.ToLower().Contains(search)); }
        var count = await source.CountAsync(ct);
        var items = await (from x in Page(source.OrderBy(x => x.Name).ThenBy(x => x.Code), q) join dept in db.Departments on x.DepartmentId equals dept.Id
            select new WarehouseDto(x.Code, x.Name, dept.Code, x.Status, x.Notes, x.Version)).ToArrayAsync(ct);
        return new(items, count, q.Page, q.PageSize);
    }
    public async Task<PagedResult<LocationDto>> LocationsAsync(MaterialQuery q, Guid departmentId, CancellationToken ct)
    {
        var source = from location in db.Set<MaterialLocation>().AsNoTracking()
                     join warehouse in db.Set<MaterialWarehouse>() on location.WarehouseId equals warehouse.Id
                     where warehouse.DepartmentId == departmentId select new { Location = location, Warehouse = warehouse };
        if (q.Status is { } status) source = source.Where(x => x.Location.Status == status && x.Warehouse.Status == status);
        if (!string.IsNullOrWhiteSpace(q.WarehouseCode)) source = source.Where(x => x.Warehouse.Code == q.WarehouseCode.Trim().ToUpper());
        if (!string.IsNullOrWhiteSpace(q.Search)) { var search = q.Search.Trim().ToLower(); source = source.Where(x => x.Location.Code.ToLower().Contains(search) || x.Location.Name.ToLower().Contains(search) || x.Warehouse.Name.ToLower().Contains(search)); }
        var count = await source.CountAsync(ct);
        var items = await (from x in Page(source.OrderBy(x => x.Warehouse.Name).ThenBy(x => x.Location.Name).ThenBy(x => x.Location.Code), q)
            join area in db.Areas on x.Location.AreaId equals area.Id into areas from area in areas.DefaultIfEmpty()
            select new LocationDto(x.Location.Code, x.Location.Name, x.Warehouse.Code, x.Warehouse.Name, area == null ? null : area.Code, area == null ? null : area.Name, x.Location.Status, x.Location.Version)).ToArrayAsync(ct);
        return new(items, count, q.Page, q.PageSize);
    }
    public async Task<PagedResult<BalanceDto>> BalancesAsync(MaterialQuery q, Guid departmentId, CancellationToken ct)
    {
        var source = from balance in db.Set<MaterialBalance>().AsNoTracking() join item in db.Set<MaterialItem>() on balance.MaterialId equals item.Id
            join location in db.Set<MaterialLocation>() on balance.LocationId equals location.Id
            join warehouse in db.Set<MaterialWarehouse>() on location.WarehouseId equals warehouse.Id
            join unit in db.Set<MaterialUnit>() on item.UnitId equals unit.Id
            where warehouse.DepartmentId == departmentId select new { balance, item, location, warehouse, unit };
        if (!string.IsNullOrWhiteSpace(q.MaterialCode)) source = source.Where(x => x.item.Code == q.MaterialCode.Trim().ToUpper());
        if (!string.IsNullOrWhiteSpace(q.WarehouseCode)) source = source.Where(x => x.warehouse.Code == q.WarehouseCode.Trim().ToUpper());
        if (!string.IsNullOrWhiteSpace(q.LocationCode)) source = source.Where(x => x.location.Code == q.LocationCode.Trim().ToUpper());
        if (q.MaterialKind is { } kind) source = source.Where(x => x.item.Kind == kind);
        if (!string.IsNullOrWhiteSpace(q.Search)) { var search = q.Search.Trim().ToLower(); source = source.Where(x => x.item.Name.ToLower().Contains(search) || x.item.Code.ToLower().Contains(search)); }
        var count = await source.CountAsync(ct);
        return new(await Page(source.OrderBy(x => x.item.Name).ThenBy(x => x.item.Code).ThenBy(x => x.location.Code), q)
            .Select(x => new BalanceDto(x.item.Code, x.item.Name, x.item.Kind, x.unit.Code, x.unit.Name, x.warehouse.Code, x.warehouse.Name, x.location.Code, x.location.Name, x.balance.Quantity)).ToArrayAsync(ct), count, q.Page, q.PageSize);
    }
    public async Task<PagedResult<MaterialReportRow>> ReportAsync(MaterialQuery q, Guid departmentId, CancellationToken ct)
    {
        var source = from entry in db.Set<MaterialLedgerEntry>().AsNoTracking() join document in db.Set<MaterialDocument>() on entry.DocumentId equals document.Id
            join item in db.Set<MaterialItem>() on entry.MaterialId equals item.Id join unit in db.Set<MaterialUnit>() on item.UnitId equals unit.Id
            join location in db.Set<MaterialLocation>() on entry.LocationId equals location.Id join warehouse in db.Set<MaterialWarehouse>() on location.WarehouseId equals warehouse.Id
            where warehouse.DepartmentId == departmentId select new { entry, document, item, unit, location, warehouse };
        if (q.To is { } to) source = source.Where(x => x.entry.OccurredAt <= to);
        if (!string.IsNullOrWhiteSpace(q.MaterialCode)) source = source.Where(x => x.item.Code == q.MaterialCode.Trim().ToUpper());
        if (!string.IsNullOrWhiteSpace(q.WarehouseCode)) source = source.Where(x => x.warehouse.Code == q.WarehouseCode.Trim().ToUpper());
        if (!string.IsNullOrWhiteSpace(q.LocationCode)) source = source.Where(x => x.location.Code == q.LocationCode.Trim().ToUpper());
        if (!string.IsNullOrWhiteSpace(q.Search)) { var search = q.Search.Trim().ToLower(); source = source.Where(x => x.item.Name.ToLower().Contains(search) || x.item.Code.ToLower().Contains(search)); }
        var grouped = source.GroupBy(x => new { x.item.Code, x.item.Name, UnitCode = x.unit.Code, UnitName = x.unit.Name,
            WarehouseCode = x.warehouse.Code, WarehouseName = x.warehouse.Name, LocationCode = x.location.Code, LocationName = x.location.Name });
        var count = await grouped.CountAsync(ct);
        var ordered = grouped.OrderBy(x => x.Key.Code).ThenBy(x => x.Key.LocationCode);
        var items = await Page(ordered, q).Select(g => new MaterialReportRow(g.Key.Code, g.Key.Name, g.Key.UnitCode, g.Key.UnitName,
            g.Key.WarehouseCode, g.Key.WarehouseName, g.Key.LocationCode, g.Key.LocationName,
            g.Sum(x => q.From != null && x.entry.OccurredAt < q.From ? x.entry.Delta : 0),
            g.Sum(x => (q.From == null || x.entry.OccurredAt >= q.From) && x.document.Kind == MaterialDocumentKind.Receipt ? x.entry.Delta : 0),
            g.Sum(x => (q.From == null || x.entry.OccurredAt >= q.From) && x.document.Kind == MaterialDocumentKind.Issue ? -x.entry.Delta : 0),
            g.Sum(x => (q.From == null || x.entry.OccurredAt >= q.From) && (x.document.Kind == MaterialDocumentKind.ReturnUnused || x.document.Kind == MaterialDocumentKind.ReceiveRecovered) ? x.entry.Delta : 0),
            g.Sum(x => (q.From == null || x.entry.OccurredAt >= q.From) && x.document.Kind == MaterialDocumentKind.Transfer && x.entry.Delta > 0 ? x.entry.Delta : 0),
            g.Sum(x => (q.From == null || x.entry.OccurredAt >= q.From) && x.document.Kind == MaterialDocumentKind.Transfer && x.entry.Delta < 0 ? -x.entry.Delta : 0),
            g.Sum(x => (q.From == null || x.entry.OccurredAt >= q.From) && x.document.Kind == MaterialDocumentKind.Adjustment && x.entry.Delta > 0 ? x.entry.Delta : 0),
            g.Sum(x => (q.From == null || x.entry.OccurredAt >= q.From) && x.document.Kind == MaterialDocumentKind.Adjustment && x.entry.Delta < 0 ? -x.entry.Delta : 0),
            g.Sum(x => x.entry.Delta))).ToArrayAsync(ct);
        return new(items, count, q.Page, q.PageSize);
    }
    public async Task<PagedResult<MaterialMovementDto>> MovementsAsync(MaterialQuery q, Guid departmentId, CancellationToken ct)
    {
        var source = from entry in db.Set<MaterialLedgerEntry>().AsNoTracking() join document in db.Set<MaterialDocument>() on entry.DocumentId equals document.Id
            join line in db.Set<MaterialDocumentLine>() on entry.LineId equals line.Id
            join location in db.Set<MaterialLocation>() on entry.LocationId equals location.Id
            join warehouse in db.Set<MaterialWarehouse>() on location.WarehouseId equals warehouse.Id
            join actor in db.Accounts on entry.ActorAccountId equals actor.Id
            where warehouse.DepartmentId == departmentId select new { entry, document, line, location, warehouse, actor };
        if (q.From is { } from) source = source.Where(x => x.entry.OccurredAt >= from);
        if (q.To is { } to) source = source.Where(x => x.entry.OccurredAt <= to);
        if (!string.IsNullOrWhiteSpace(q.MaterialCode)) source = source.Where(x => x.line.MaterialCodeSnapshot == q.MaterialCode.Trim().ToUpper());
        if (!string.IsNullOrWhiteSpace(q.WarehouseCode)) source = source.Where(x => x.warehouse.Code == q.WarehouseCode.Trim().ToUpper());
        if (!string.IsNullOrWhiteSpace(q.LocationCode)) source = source.Where(x => x.location.Code == q.LocationCode.Trim().ToUpper());
        if (q.Kind is { } kind) source = source.Where(x => x.document.Kind == kind);
        if (!string.IsNullOrWhiteSpace(q.Search)) { var search = q.Search.Trim().ToLower(); source = source.Where(x => x.line.MaterialNameSnapshot.ToLower().Contains(search) || x.document.Code.ToLower().Contains(search)); }
        var count = await source.CountAsync(ct);
        var items = await Page(source.OrderByDescending(x => x.entry.OccurredAt).ThenBy(x => x.entry.Id), q).Select(x => new MaterialMovementDto(x.document.Code, x.document.Kind, x.line.MaterialCodeSnapshot, x.line.MaterialNameSnapshot, x.line.UnitNameSnapshot, x.location.Code, x.location.Name, x.entry.Delta, x.entry.BalanceAfter, x.actor.AccountCode, x.entry.OccurredAt)).ToArrayAsync(ct);
        return new(items, count, q.Page, q.PageSize);
    }
    private IQueryable<MaterialDocumentSummary> Summaries(IQueryable<MaterialDocument> source) =>
        from doc in source join creator in db.Accounts on doc.CreatedByAccountId equals creator.Id
        join poster in db.Accounts on doc.PostedByAccountId equals poster.Id into posters from poster in posters.DefaultIfEmpty()
        join request in db.Set<CSVC.Core.ProcurementRequests.ProcurementRequest>() on doc.ProcurementRequestId equals request.Id into requests from request in requests.DefaultIfEmpty()
        join department in db.Departments on doc.TargetDepartmentId equals department.Id into departments from department in departments.DefaultIfEmpty()
        select new MaterialDocumentSummary(doc.Code, doc.Kind, doc.Status, doc.Reason, creator.AccountCode, doc.CreatedAt, poster == null ? null : poster.AccountCode, doc.PostedAt,
            request == null ? null : request.DisplayCode, department == null ? null : department.Code, department == null ? null : department.Name,
            db.Set<MaterialDocumentLine>().Count(l => l.DocumentId == doc.Id), doc.Version);
    public async Task<PagedResult<MaterialDocumentSummary>> DocumentsAsync(MaterialQuery q, MaterialActor actor, CancellationToken ct)
    {
        var source = db.Set<MaterialDocument>().AsNoTracking().Where(d => actor.Department ? d.TargetDepartmentId == actor.DepartmentId : d.DepartmentId == actor.DepartmentId);
        if (q.Kind is { } kind) source = source.Where(x => x.Kind == kind);
        if (q.DocumentStatus is { } status) source = source.Where(x => x.Status == status);
        if (q.From is { } from) source = source.Where(x => x.CreatedAt >= from);
        if (q.To is { } to) source = source.Where(x => x.CreatedAt <= to);
        if (!string.IsNullOrWhiteSpace(q.RequestCode)) source = source.Where(x => db.Set<CSVC.Core.ProcurementRequests.ProcurementRequest>().Any(r => r.Id == x.ProcurementRequestId && r.DisplayCode == q.RequestCode.Trim().ToUpper()));
        if (!string.IsNullOrWhiteSpace(q.ServiceOrderCode)) source = source.Where(x => db.Set<CSVC.Core.ServiceOrders.ServiceOrder>().Any(r => r.Id == x.ServiceOrderId && r.DisplayCode == q.ServiceOrderCode.Trim().ToUpper()));
        if (!string.IsNullOrWhiteSpace(q.Search)) { var search = q.Search.Trim().ToLower(); source = source.Where(x => x.Code.ToLower().Contains(search) || x.Reason.ToLower().Contains(search)); }
        var count = await source.CountAsync(ct);
        return new(await Summaries(Page(source.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Code), q)).ToArrayAsync(ct), count, q.Page, q.PageSize);
    }
    public Task<MaterialDocumentSummary> SummaryAsync(Guid id, CancellationToken ct) => Summaries(db.Set<MaterialDocument>().AsNoTracking().Where(x => x.Id == id)).SingleAsync(ct);
    public Task<MaterialDocument?> DocumentAsync(string code, bool tracked, CancellationToken ct) =>
        (tracked ? db.Set<MaterialDocument>() : db.Set<MaterialDocument>().AsNoTracking()).Include(d => d.Lines).SingleOrDefaultAsync(d => d.Code == code, ct);
    public Task<MaterialBalance?> BalanceAsync(Guid materialId, Guid locationId, CancellationToken ct) => db.Set<MaterialBalance>().SingleOrDefaultAsync(b => b.MaterialId == materialId && b.LocationId == locationId, ct);
    public async Task<IReadOnlyList<MaterialDocument>> RequestDocumentsAsync(Guid requestId, CancellationToken ct) => await db.Set<MaterialDocument>().AsNoTracking().Include(d => d.Lines).Where(d => d.ProcurementRequestId == requestId).ToArrayAsync(ct);
    public async Task<IReadOnlyList<MaterialDocument>> ServiceDocumentsAsync(Guid serviceId, CancellationToken ct) => await db.Set<MaterialDocument>().AsNoTracking().Include(d => d.Lines).Where(d => d.ServiceOrderId == serviceId).ToArrayAsync(ct);
    public async Task<decimal> UsedAsync(Guid issueLineId, Guid? excludeChangeId, CancellationToken ct)
        => (await db.Set<EquipmentMaterialUse>().Where(u => u.IssueLineId == issueLineId && u.ChangeId != excludeChangeId && !db.Set<EquipmentChange>().Any(c => c.ReplacesId == u.ChangeId)).SumAsync(u => (decimal?)u.Quantity, ct) ?? 0)
            + (await db.Set<MaterialWorkUse>().Where(u => u.IssueLineId == issueLineId && !db.Set<MaterialWorkUse>().Any(r => r.ReplacesId == u.Id)).SumAsync(u => (decimal?)u.Quantity, ct) ?? 0);
    public async Task<decimal> ReturnedAsync(Guid issueLineId, MaterialDocumentKind kind, CancellationToken ct) =>
        await (from line in db.Set<MaterialDocumentLine>() join doc in db.Set<MaterialDocument>() on line.DocumentId equals doc.Id
            where line.OriginalIssueLineId == issueLineId && doc.Kind == kind && doc.PostedAt != null select (decimal?)line.Quantity).SumAsync(ct) ?? 0;
    public async Task<decimal> RecoveredAsync(Guid removedId, CancellationToken ct) =>
        await (from line in db.Set<MaterialDocumentLine>() join doc in db.Set<MaterialDocument>() on line.DocumentId equals doc.Id
            where line.RemovedMaterialId == removedId && doc.PostedAt != null select (decimal?)line.Quantity).SumAsync(ct) ?? 0;
    public Task<MaterialDocument?> IssueOfAsync(Guid lineId, CancellationToken ct) => db.Set<MaterialDocument>().AsNoTracking().Include(d => d.Lines).SingleOrDefaultAsync(d => d.Lines.Any(l => l.Id == lineId), ct);
    public Task<EquipmentChange?> ChangeAsync(string code, CancellationToken ct) => db.Set<EquipmentChange>().AsNoTracking().Include(c => c.Uses).Include(c => c.Removed).SingleOrDefaultAsync(c => c.Code == code, ct);
    public Task<EquipmentChange?> ChangeOfRemovalAsync(Guid removalId, CancellationToken ct) => db.Set<EquipmentChange>().AsNoTracking().Include(c => c.Uses).Include(c => c.Removed).SingleOrDefaultAsync(c => c.Removed.Any(r => r.Id == removalId), ct);
    public async Task<IReadOnlyList<EquipmentChange>> ActiveChangesAsync(Guid? serviceId, Guid? deviceId, CancellationToken ct) => await db.Set<EquipmentChange>().AsNoTracking().Include(c => c.Uses).Include(c => c.Removed)
        .Where(c => c.ServiceOrderId == serviceId && (deviceId == null || c.DeviceId == deviceId) && !db.Set<EquipmentChange>().Any(r => r.ReplacesId == c.Id)).ToArrayAsync(ct);
    public async Task<PagedResult<EquipmentChange>> ChangesAsync(Guid? deviceId, MaterialQuery q, CancellationToken ct)
    {
        var source = db.Set<EquipmentChange>().AsNoTracking().Where(c => deviceId == null || c.DeviceId == deviceId);
        if (!string.IsNullOrWhiteSpace(q.ServiceOrderCode)) source = source.Where(c => db.Set<CSVC.Core.ServiceOrders.ServiceOrder>().Any(o => o.Id == c.ServiceOrderId && o.DisplayCode == q.ServiceOrderCode.Trim().ToUpper()));
        if (q.From is { } from) source = source.Where(c => c.PerformedAt >= from);
        if (q.To is { } to) source = source.Where(c => c.PerformedAt <= to);
        if (!string.IsNullOrWhiteSpace(q.Search)) { var search = q.Search.Trim().ToLower(); source = source.Where(c => c.Target.ToLower().Contains(search) || c.Reason.ToLower().Contains(search)); }
        var count = await source.CountAsync(ct);
        return new(await Page(source.OrderByDescending(c => c.PerformedAt).ThenBy(c => c.Code), q).Include(c => c.Uses).Include(c => c.Removed).ToArrayAsync(ct), count, q.Page, q.PageSize);
    }
    public async Task<PagedResult<MaterialHistoryDto>> HistoryAsync(Guid documentId, int page, int pageSize, CancellationToken ct)
    {
        var source = db.Set<MaterialDocumentHistory>().AsNoTracking().Where(h => h.DocumentId == documentId);
        var count = await source.CountAsync(ct);
        var items = await (from h in source join actor in db.Accounts on h.ActorAccountId equals actor.Id orderby h.OccurredAt descending, h.Id
            select new MaterialHistoryDto(h.Action, h.Reason, actor.AccountCode, h.OccurredAt, h.BeforeData, h.AfterData)).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        return new(items, count, page, pageSize);
    }
}
