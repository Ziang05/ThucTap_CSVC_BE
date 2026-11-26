using CSVC.Core.Materials;
using CSVC.Core.ProcurementRequests;
using CSVC.Core.ServiceOrders;
using CSVC.UseCases.ProcurementRequests;

namespace CSVC.UseCases.Materials;

public sealed class MaterialWorkflow(IMaterialStore store) : IMaterialWorkflow
{
    public async Task RequireServiceReconciledAsync(ServiceOrder order, IReadOnlyList<Guid> deviceIds, CancellationToken ct)
        => await ReconcileAsync(order.Id, deviceIds, ct);
    private async Task ReconcileAsync(Guid serviceId, IReadOnlyList<Guid>? deviceIds, CancellationToken ct)
    {
        var docs = await store.ServiceDocumentsAsync(serviceId, ct);
        foreach (var doc in docs.Where(d => d.Status is not (MaterialDocumentStatus.Cancelled or MaterialDocumentStatus.Rejected)))
        {
            var lines = doc.Lines.Where(l => deviceIds is null || l.DeviceId == null || deviceIds.Contains(l.DeviceId.Value)).ToArray();
            if (lines.Length == 0) continue;
            if (doc.Status != MaterialDocumentStatus.Completed) throw MaterialErrors.Conflict("MATERIAL_RECONCILIATION_REQUIRED", "Complete or cancel all related material documents before closing this work.");
            if (doc.Kind == MaterialDocumentKind.Issue && doc.ProcurementRequestId is null)
                foreach (var line in lines)
                    if (await store.UsedAsync(line.Id, null, ct) + await store.ReturnedAsync(line.Id, MaterialDocumentKind.ReturnUnused, ct) != line.Quantity)
                        throw MaterialErrors.Conflict("MATERIAL_RECONCILIATION_REQUIRED", "Issued materials must be fully reconciled as used or returned unused before closing this work.");
        }
        foreach (var change in await store.ActiveChangesAsync(serviceId, null, ct))
        {
            if (deviceIds is not null && !deviceIds.Contains(change.DeviceId)) continue;
            foreach (var part in change.Removed.Where(r => r.Disposition == RemovedDisposition.ReturnToStock))
                if (await store.RecoveredAsync(part.Id, ct) != part.Quantity) throw MaterialErrors.Conflict("MATERIAL_RECOVERY_REQUIRED", "Receive all removed parts marked for return to stock before closing the work.");
        }
    }
    public async Task ValidateServiceResultsAsync(ServiceOrder order, IReadOnlyList<ServiceOrderItem> items, CancellationToken ct)
    {
        foreach (var item in items)
        {
            var existing = await store.ActiveChangesAsync(order.Id, item.DeviceId, ct);
            if (item.ExecutionStatus == ServiceItemStatus.NotExecuted && existing.Count > 0) throw MaterialErrors.Invalid("Equipment interventions already recorded; the result cannot be NotExecuted.");
        }
    }
    public async Task<IReadOnlyList<RequestDocumentDto>> RequestDocumentsAsync(ProcurementRequest request, CancellationToken ct)
        => (await store.RequestDocumentsAsync(request.Id, ct)).Select(d => new RequestDocumentDto(
            d.Kind == MaterialDocumentKind.Receipt ? RequestDocumentKind.MaterialReceipt : d.Kind == MaterialDocumentKind.Issue ? RequestDocumentKind.MaterialIssue : RequestDocumentKind.MaterialReturn,
            d.Code, d.Status.ToString(), d.PostedAt != null, d.Status is not (MaterialDocumentStatus.Completed or MaterialDocumentStatus.Cancelled or MaterialDocumentStatus.Rejected), [],
            d.Lines.Select(l => new RequestMaterialBinding(l.SourceLineCode, l.MaterialCodeSnapshot, l.Quantity, l.UnitNameSnapshot)).ToArray())).ToArray();
    public async Task RequireRequestReadyAsync(ProcurementRequest request, CancellationToken ct)
    {
        var lines = request.Lines.Where(l => l.Kind == ProcurementLineKind.Material).ToArray();
        if (lines.Length == 0) return;
        var issue = (await store.RequestDocumentsAsync(request.Id, ct)).SingleOrDefault(d => d.Kind == MaterialDocumentKind.Issue && d.Status == MaterialDocumentStatus.Completed)
            ?? throw MaterialErrors.Conflict("SOURCE_REQUEST_INCOMPLETE", "Complete the material issue before closing this request.");
        foreach (var line in lines)
            if (issue.Lines.Where(l => l.SourceLineCode == line.SourceLineCode).Sum(l => l.Quantity) != line.Quantity) throw MaterialErrors.Conflict("SOURCE_REQUEST_INCOMPLETE", "All material request lines must be delivered in full.");
    }
}
