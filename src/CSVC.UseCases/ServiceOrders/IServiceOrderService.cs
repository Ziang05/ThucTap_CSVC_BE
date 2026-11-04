using CSVC.UseCases.Common;
using CSVC.UseCases.ServiceOrders.DTOs;

namespace CSVC.UseCases.ServiceOrders;

public interface IServiceOrderService
{
    Task<PagedResult<ServiceOrderDto>> ListAsync(ServiceOrderListQuery query, CancellationToken ct);
    Task<ServiceOrderDto> GetAsync(string code, CancellationToken ct);
    Task<ServiceOrderDto> CreateAsync(CreateServiceOrderRequest request, CancellationToken ct);
    Task ApproveAsync(string code, ServiceTransitionRequest request, CancellationToken ct);
    Task CancelAsync(string code, CancelServiceOrderRequest request, CancellationToken ct);
    Task RecordResultsAsync(string code, RecordServiceResultsRequest request, CancellationToken ct);
    Task<PagedResult<ExportSlipDto>> ListExportsAsync(ExportSlipListQuery query, CancellationToken ct);
    Task<ExportSlipDto> GetExportAsync(string code, CancellationToken ct);
    Task<ExportSlipDto> CreateExportAsync(CreateExportSlipRequest request, CancellationToken ct);
    Task ApproveExportAsync(string code, ServiceTransitionRequest request, CancellationToken ct);
    Task EditExportAsync(string code, EditExportSlipRequest request, CancellationToken ct);
    Task CancelExportAsync(string code, CancelServiceOrderRequest request, CancellationToken ct);
    Task<int> ReconcileHoldsAsync(string code, ServiceTransitionRequest request, CancellationToken ct);
    Task<ExportReturnDto> CreateReturnAsync(CreateExportReturnRequest request, CancellationToken ct);
    Task<ExportReturnDto> GetReturnAsync(string code, CancellationToken ct);
    Task<PagedResult<ExportReturnDto>> ListReturnsAsync(ExportReturnListQuery query, CancellationToken ct);
    Task<PagedResult<ServiceHistoryDto>> ReturnHistoryAsync(string code, ServiceHistoryQuery query, CancellationToken ct);
    Task RecordExportResultAsync(string code, RecordExportResultRequest request, CancellationToken ct);
    Task<PagedResult<ServiceHistoryDto>> HistoryAsync(string code, ServiceHistoryQuery query, CancellationToken ct);
    Task<PagedResult<ServiceHistoryDto>> ExportHistoryAsync(string code, ServiceHistoryQuery query, CancellationToken ct);
}
