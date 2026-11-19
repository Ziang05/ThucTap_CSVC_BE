using CSVC.Core.Materials;
using CSVC.Core.ProcurementRequests;
using CSVC.Core.ServiceOrders;
using CSVC.UseCases.Common;
using CSVC.UseCases.ProcurementRequests;

namespace CSVC.UseCases.Materials;

public interface IMaterialStore
{
    Task<PagedResult<UnitDto>> UnitsAsync(MaterialQuery query, CancellationToken ct);
    Task<PagedResult<MaterialDto>> MaterialsAsync(MaterialQuery query, CancellationToken ct);
    Task<PagedResult<WarehouseDto>> WarehousesAsync(MaterialQuery query, Guid departmentId, CancellationToken ct);
    Task<PagedResult<LocationDto>> LocationsAsync(MaterialQuery query, Guid departmentId, CancellationToken ct);
    Task<PagedResult<BalanceDto>> BalancesAsync(MaterialQuery query, Guid departmentId, CancellationToken ct);
    Task<PagedResult<MaterialReportRow>> ReportAsync(MaterialQuery query, Guid departmentId, CancellationToken ct);
    Task<PagedResult<MaterialMovementDto>> MovementsAsync(MaterialQuery query, Guid departmentId, CancellationToken ct);
    Task<PagedResult<MaterialDocumentSummary>> DocumentsAsync(MaterialQuery query, MaterialActor actor, CancellationToken ct);
    Task<MaterialDocument?> DocumentAsync(string code, bool tracked, CancellationToken ct);
    Task<MaterialDocumentSummary> SummaryAsync(Guid id, CancellationToken ct);
    Task<MaterialBalance?> BalanceAsync(Guid materialId, Guid locationId, CancellationToken ct);
    Task<IReadOnlyList<MaterialDocument>> RequestDocumentsAsync(Guid requestId, CancellationToken ct);
    Task<IReadOnlyList<MaterialDocument>> ServiceDocumentsAsync(Guid serviceId, CancellationToken ct);
    Task<decimal> UsedAsync(Guid issueLineId, Guid? excludeChangeId, CancellationToken ct);
    Task<decimal> ReturnedAsync(Guid issueLineId, MaterialDocumentKind kind, CancellationToken ct);
    Task<decimal> RecoveredAsync(Guid removedId, CancellationToken ct);
    Task<MaterialDocument?> IssueOfAsync(Guid lineId, CancellationToken ct);
    Task<EquipmentChange?> ChangeAsync(string code, CancellationToken ct);
    Task<EquipmentChange?> ChangeOfRemovalAsync(Guid removalId, CancellationToken ct);
    Task<IReadOnlyList<EquipmentChange>> ActiveChangesAsync(Guid? serviceId, Guid? deviceId, CancellationToken ct);
    Task<PagedResult<EquipmentChange>> ChangesAsync(Guid? deviceId, MaterialQuery query, CancellationToken ct);
    Task<PagedResult<MaterialHistoryDto>> HistoryAsync(Guid documentId, int page, int pageSize, CancellationToken ct);
}
public interface IMaterialWorkflow
{
    Task RequireServiceReconciledAsync(ServiceOrder order, IReadOnlyList<Guid> deviceIds, CancellationToken ct);
    Task ValidateServiceResultsAsync(ServiceOrder order, IReadOnlyList<ServiceOrderItem> items, CancellationToken ct);
    Task<IReadOnlyList<RequestDocumentDto>> RequestDocumentsAsync(ProcurementRequest request, CancellationToken ct);
    Task RequireRequestReadyAsync(ProcurementRequest request, CancellationToken ct);
}
// Only the server's combined receipt operation may confirm both equipment and materials together.
public sealed class MaterialDeliveryScope
{
    public Guid? RequestId { get; set; }
}
