using CSVC.Core.ServiceOrders;
using CSVC.UseCases.Common;
using CSVC.UseCases.ServiceOrders.DTOs;

namespace CSVC.UseCases.Abstractions.Persistence;

public interface IServiceOrderRepository : IRepository<ServiceOrder>
{
    Task<ServiceOrder?> GetAggregateAsync(string code, bool tracked, CancellationToken ct);
    Task<ServiceOrder?> GetByCreateRequestAsync(Guid requestId, CancellationToken ct);
    Task<ServiceOrder?> GetByExportCodeAsync(string code, bool tracked, CancellationToken ct);
    Task<PagedResult<ServiceOrder>> ListAsync(ServiceOrderListQuery query, CancellationToken ct);
    Task<PagedResult<ExportSlipDto>> ListExportsAsync(ExportSlipListQuery query, CancellationToken ct);
    Task<PagedResult<ServiceHistoryDto>> HistoryAsync(Guid orderId, ServiceHistoryQuery query, CancellationToken ct);
    Task<PagedResult<ServiceHistoryDto>> ExportHistoryAsync(Guid slipId, ServiceHistoryQuery query, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, string>> AccountCodesAsync(IEnumerable<Guid> ids, CancellationToken ct);
}
