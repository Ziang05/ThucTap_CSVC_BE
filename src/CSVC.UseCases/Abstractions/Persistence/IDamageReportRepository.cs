using CSVC.Core.DamageReports;
using CSVC.UseCases.Common;
using CSVC.UseCases.DamageReports;

namespace CSVC.UseCases.Abstractions.Persistence;

public interface IDamageReportRepository : IRepository<DamageReport>
{
    Task<DamageReport?> GetAggregateAsync(string code, Guid? departmentScope, bool tracked, CancellationToken ct);
    Task<DamageReportRequest?> RequestAsync(Guid requestId, CancellationToken ct);
    Task AddRequestAsync(DamageReportRequest request, CancellationToken ct);
    Task AddHistoryAsync(DamageReportHistory history, CancellationToken ct);
    Task<bool> HasPendingAsync(Guid deviceId, CancellationToken ct);
    Task<bool> HasRepairAsync(Guid reportId, CancellationToken ct);
    void RemovePending(DamageReportPendingDevice pending);
    Task<PagedResult<DamageReportSummaryDto>> ListAsync(DamageReportListQuery query, Guid? departmentScope, CancellationToken ct);
    Task<PagedResult<ReportDeviceOptionDto>> DevicesAsync(int page, int pageSize, string? search, Guid? departmentScope, CancellationToken ct);
    Task<PagedResult<ReportHistoryDto>> HistoryAsync(Guid id, int page, int pageSize, CancellationToken ct);
}
