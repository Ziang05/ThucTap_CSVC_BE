using CSVC.Core.DamageReports;
using CSVC.UseCases.Abstractions.Persistence;
using CSVC.UseCases.Common;
using CSVC.UseCases.DamageReports;
using CSVC.UseCases.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace CSVC.Infrastructure.Persistence.Repositories;

public sealed class DamageReportRepository(CsvcDbContext db) : GenericRepository<DamageReport>(db), IDamageReportRepository
{
    public Task<DamageReport?> GetAggregateAsync(string code, Guid? scope, bool tracked, CancellationToken ct)
    {
        var q = _context.DamageReports.Where(x => x.ReportCode == code && (scope == null || x.ReportingDepartmentId == scope));
        if (!tracked) q = q.AsNoTracking();
        return q.Include(x => x.Context).Include(x => x.Pending).Include(x => x.Reception).Include(x => x.Evidence).AsSingleQuery().SingleOrDefaultAsync(ct);
    }
    public Task<DamageReportRequest?> RequestAsync(Guid id, CancellationToken ct) => _context.Set<DamageReportRequest>().AsNoTracking().SingleOrDefaultAsync(x => x.RequestId == id, ct);
    public async Task AddRequestAsync(DamageReportRequest request, CancellationToken ct) => await _context.AddAsync(request, ct);
    public async Task AddHistoryAsync(DamageReportHistory history, CancellationToken ct) => await _context.AddAsync(history, ct);
    public Task<bool> HasPendingAsync(Guid deviceId, CancellationToken ct) => _context.Set<DamageReportPendingDevice>().AnyAsync(x => x.DeviceId == deviceId, ct);
    public Task<bool> HasRepairAsync(Guid id, CancellationToken ct) => _context.RepairItemDetails.AnyAsync(x => x.DamageReportId == id, ct);
    public void RemovePending(DamageReportPendingDevice pending) => _context.Remove(pending);

    public async Task<PagedResult<DamageReportSummaryDto>> ListAsync(DamageReportListQuery query, Guid? scope, CancellationToken ct)
    {
        var q = _context.DamageReports.AsNoTracking().Where(x => scope == null || x.ReportingDepartmentId == scope);
        if (query.Search?.Trim() is { Length: > 0 } search)
            q = q.Where(x => x.ReportCode.Contains(search) || x.Context.DeviceCodeSnapshot.Contains(search) || x.Context.DeviceNameSnapshot.Contains(search) || x.Context.ReporterNameSnapshot.Contains(search));
        if (query.IncidentType is { } type) { var incident = Enum.Parse<DamageReportIncidentType>(type, true); q = q.Where(x => x.IncidentType == incident); }
        if (query.DepartmentCode is { } dept) q = q.Where(x => _context.Departments.Any(d => d.Id == x.ReportingDepartmentId && d.Code == dept.Trim().ToUpper()));
        if (query.DeviceCode is { } device) q = q.Where(x => x.Context.DeviceCodeSnapshot == device.Trim().ToUpper());
        if (query.From is { } from) q = q.Where(x => x.ReportedAt >= from.ToUniversalTime());
        if (query.To is { } to) q = q.Where(x => x.ReportedAt <= to.ToUniversalTime());
        if (await q.AnyAsync(x => (x.Pending == null) == (x.Reception == null) || x.Context == null || x.Pending != null && x.Pending.DeviceId != x.DeviceId, ct))
            throw new UseCaseException(FailureKind.Conflict, "REPORT_STATE_INVALID", "Report reception data is inconsistent.");
        if (query.ReceptionStatus == "RECEIVED") q = q.Where(x => x.Reception != null);
        if (query.ReceptionStatus == "NOT_RECEIVED") q = q.Where(x => x.Pending != null);
        var count = await q.CountAsync(ct);
        var sorted = query.SortBy == "reportCode"
            ? (query.Descending ? q.OrderByDescending(x => x.ReportCode) : q.OrderBy(x => x.ReportCode))
            : (query.Descending ? q.OrderByDescending(x => x.ReportedAt).ThenByDescending(x => x.ReportCode) : q.OrderBy(x => x.ReportedAt).ThenBy(x => x.ReportCode));
        var rows = await sorted.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).Select(x => new
        {
            x.ReportCode, x.Context.DeviceCodeSnapshot, x.Context.DeviceNameSnapshot, x.IncidentType,
            x.Context.ReportingDepartmentNameSnapshot, x.Context.ReporterNameSnapshot, x.ReportedAt, Received = x.Reception != null, x.RowVersion
        }).ToListAsync(ct);
        return new(rows.Select(x => new DamageReportSummaryDto(x.ReportCode, x.DeviceCodeSnapshot, x.DeviceNameSnapshot,
            DamageReportService.Incident(x.IncidentType), x.ReportingDepartmentNameSnapshot, x.ReporterNameSnapshot, x.ReportedAt,
            x.Received ? "RECEIVED" : "NOT_RECEIVED", x.RowVersion)).ToArray(), count, query.Page, query.PageSize);
    }
    public async Task<PagedResult<ReportDeviceOptionDto>> DevicesAsync(int page, int size, string? search, Guid? scope, CancellationToken ct)
    {
        var q = _context.Devices.AsNoTracking().Where(x => scope == null || x.DepartmentId == scope);
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(x => x.DeviceCode.Contains(search) || x.Name.Contains(search));
        var count = await q.CountAsync(ct);
        var items = await q.OrderBy(x => x.DeviceCode).Skip((page - 1) * size).Take(size)
            .Select(x => new ReportDeviceOptionDto(x.DeviceCode, x.Name, !_context.Set<DamageReportPendingDevice>().Any(p => p.DeviceId == x.Id))).ToListAsync(ct);
        return new(items, count, page, size);
    }
    public async Task<PagedResult<ReportHistoryDto>> HistoryAsync(Guid id, int page, int size, CancellationToken ct)
    {
        var q = _context.Set<DamageReportHistory>().AsNoTracking().Where(x => x.DamageReportId == id);
        var count = await q.CountAsync(ct);
        var items = await q.OrderByDescending(x => x.OccurredAt).ThenBy(x => x.Id).Skip((page - 1) * size).Take(size)
            .Select(x => new ReportHistoryDto(x.ActionCode, _context.Accounts.Where(a => a.Id == x.ActorAccountId).Select(a => a.AccountCode).Single(),
                x.OccurredAt, x.ChangedFields, x.BeforeData, x.AfterData)).ToListAsync(ct);
        return new(items, count, page, size);
    }
}
