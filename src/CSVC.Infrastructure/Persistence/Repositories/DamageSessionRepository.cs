using CSVC.Core.Common;
using CSVC.Core.DamageReports;
using CSVC.Core.Devices;
using CSVC.Core.ServiceOrders;
using CSVC.UseCases.Common;
using CSVC.UseCases.DamageReports;
using Microsoft.EntityFrameworkCore;

namespace CSVC.Infrastructure.Persistence.Repositories;

public sealed class DamageSessionRepository(CsvcDbContext context) : IDamageSessionRepository
{
    public async Task<IReadOnlyList<DamageSessionOptionDto>> RepairOptionsAsync(string[] codes, CancellationToken ct)
    {
        var sessions = await (from session in context.Set<DeviceDamageSession>().AsNoTracking()
                              join device in context.Devices on session.DeviceId equals device.Id
                              where session.ClosedAt == null && codes.Contains(device.DeviceCode)
                              select new { session.Id, device.DeviceCode, session.OpenedAt }).ToListAsync(ct);
        var ids = sessions.Select(x => x.Id).ToArray();
        var reports = await (from decision in context.Set<DamageReportDecision>()
                             join report in context.DamageReports on decision.DamageReportId equals report.Id
                             where decision.DamageSessionId.HasValue && ids.Contains(decision.DamageSessionId.Value)
                             select new { decision.DamageSessionId, report.ReportCode }).ToListAsync(ct);
        var bySession = reports.ToLookup(x => x.DamageSessionId, x => x.ReportCode);
        return sessions.Select(x => new DamageSessionOptionDto(x.Id, x.DeviceCode, x.OpenedAt, bySession[x.Id].ToArray())).ToArray();
    }

    public Task<DeviceDamageSession?> FindOpenAsync(Guid deviceId, CancellationToken ct)
        => context.Set<DeviceDamageSession>().SingleOrDefaultAsync(x => x.DeviceId == deviceId && x.ClosedAt == null, ct);

    public Task<bool> HasActiveRepairAsync(Guid sessionId, CancellationToken ct)
        => (from item in context.ServiceOrderItems
            join order in context.ServiceOrders on item.ServiceOrderId equals order.Id
            where item.DamageSessionId == sessionId && order.OrderType == ServiceOrderType.Repair
                && (order.Status == ServiceWorkflowStatus.PendingApproval
                    || order.Status == ServiceWorkflowStatus.InProgress && item.ExecutionStatus == ServiceItemStatus.InProgress)
            select item.Id).AnyAsync(ct);

    public async Task<PagedResult<DamageSessionDto>> ListAsync(DamageSessionQuery query, Guid? departmentScope,
        bool includeDetails, Guid? sessionId, CancellationToken ct)
    {
        var code = query.DeviceCode?.Trim().ToUpperInvariant();
        var source = from session in context.Set<DeviceDamageSession>().AsNoTracking()
                     join device in context.Devices on session.DeviceId equals device.Id
                     join department in context.Departments on device.DepartmentId equals department.Id
                     where (departmentScope == null || department.Id == departmentScope)
                         && (sessionId == null || session.Id == sessionId)
                         && (code == null || device.DeviceCode == code)
                         && (query.Status == null || (query.Status == DamageSessionStatus.Closed) == (session.ClosedAt != null))
                     select new
                     {
                         Session = session, device.DeviceCode, device.Name, device.SerialNumber,
                         DepartmentCode = department.Code, Department = department.Name,
                         Eligible = device.Quantity == 1 && device.Condition == DeviceCondition.Damaged,
                         Busy = includeDetails && (context.DeviceOperationHolds.Any(hold => hold.DeviceId == device.Id)
                             || context.Set<ServiceOrderDeviceHold>().Any(hold => hold.DeviceId == device.Id)
                             || context.Set<ExportDeviceGuard>().Any(guard => guard.DeviceId == device.Id))
                     };
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = TextNormalization.Name(query.Search).ToLowerInvariant();
            source = source.Where(x => x.Name.ToLower().Contains(search) || x.DeviceCode.ToLower().Contains(search));
        }
        var count = await source.CountAsync(ct);
        var sorted = query.Descending ? source.OrderByDescending(x => x.Session.OpenedAt) : source.OrderBy(x => x.Session.OpenedAt);
        var page = await sorted.ThenBy(x => x.Session.Id).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        var ids = page.Select(x => x.Session.Id).ToArray();
        var attempts = includeDetails && ids.Length > 0 ? await Attempts(ids).ToListAsync(ct) : [];
        var relatedAttempts = attempts.ToLookup(x => x.SessionId, x => x.Attempt);
        var reports = includeDetails && ids.Length > 0
            ? await (from decision in context.Set<DamageReportDecision>()
                     join report in context.DamageReports on decision.DamageReportId equals report.Id
                     where decision.DamageSessionId.HasValue && ids.Contains(decision.DamageSessionId.Value)
                     orderby decision.DecidedAt, report.ReportCode
                     select new { Id = decision.DamageSessionId!.Value, report.ReportCode }).ToListAsync(ct) : [];
        var relatedReports = reports.ToLookup(x => x.Id, x => x.ReportCode);
        var inspections = includeDetails && ids.Length > 0
            ? await (from inspection in context.Set<DamageSessionInspection>()
                     join item in context.ServiceOrderItems on inspection.ServiceOrderItemId equals item.Id
                     join order in context.ServiceOrders on item.ServiceOrderId equals order.Id
                     join account in context.Accounts on inspection.InspectedByAccountId equals account.Id
                     where ids.Contains(inspection.DamageSessionId)
                     orderby inspection.InspectedAt descending, inspection.RequestId
                     select new
                     {
                         inspection.DamageSessionId,
                         Detail = new DamageSessionInspectionDto(inspection.RequestId, item.Id, order.DisplayCode, item.ItemNo,
                             inspection.Passed, inspection.Notes, inspection.InspectedAt, account.AccountCode)
                     }).ToListAsync(ct) : [];
        var relatedInspections = inspections.ToLookup(x => x.DamageSessionId, x => x.Detail);
        var items = page.Select(row =>
        {
            var related = relatedAttempts[row.Session.Id].ToArray();
            var latest = related.FirstOrDefault(attempt => attempt.OrderStatus != ServiceWorkflowStatus.Canceled);
            var open = row.Session.ClosedAt == null;
            var awaitingReturn = open && latest?.AwaitingReturn == true;
            DamageSessionRepairBlock? block = null;
            if (includeDetails && open)
            {
                if (awaitingReturn) block = DamageSessionRepairBlock.AwaitingReturn;
                else if (related.Any(attempt => attempt.OrderStatus == ServiceWorkflowStatus.PendingApproval
                    || attempt.ItemStatus == ServiceItemStatus.InProgress))
                    block = DamageSessionRepairBlock.ActiveRepair;
                else if (row.Busy) block = DamageSessionRepairBlock.DeviceBusy;
                else if (!row.Eligible) block = DamageSessionRepairBlock.DeviceUnavailable;
            }
            return new DamageSessionDto(row.Session.Id, row.DeviceCode, row.Name, row.Department,
                row.Session.OpenedAt, row.Session.ClosedAt, row.Session.Version, relatedReports[row.Session.Id].ToArray(),
                related, relatedInspections[row.Session.Id].ToArray(), awaitingReturn)
            {
                SerialNumber = row.SerialNumber,
                DepartmentCode = row.DepartmentCode,
                RepairBlock = block,
                CanCreateRepair = includeDetails && open && block is null
            };
        }).ToArray();
        return new(items, count, query.Page, query.PageSize);
    }

    private IQueryable<AttemptRow> Attempts(Guid[] sessionIds)
        => from item in context.ServiceOrderItems
           join order in context.ServiceOrders on item.ServiceOrderId equals order.Id
           where item.DamageSessionId.HasValue && sessionIds.Contains(item.DamageSessionId.Value) && order.OrderType == ServiceOrderType.Repair
           orderby order.CreatedAt descending, order.DisplayCode descending, item.Id
           select new AttemptRow(item.DamageSessionId!.Value, new DamageRepairAttemptDto(item.Id, order.DisplayCode, item.ItemNo,
               order.Status, item.ExecutionStatus, item.ExecutionMethod == ExecutionMethod.External &&
                   context.Set<ExportSlipItem>().Any(line => line.ServiceOrderItemId == item.Id &&
                       line.Result == ExportResult.Success && line.ReturnItem == null),
               order.CreatedAt));

    private sealed record AttemptRow(Guid SessionId, DamageRepairAttemptDto Attempt);
}
