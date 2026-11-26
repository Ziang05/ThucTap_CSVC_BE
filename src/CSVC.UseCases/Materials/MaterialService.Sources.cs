using CSVC.Core.Materials;
using CSVC.Core.ProcurementRequests;
using CSVC.UseCases.DeviceTransfers.DTOs;
using CSVC.UseCases.Devices.DTOs;

namespace CSVC.UseCases.Materials;

public sealed partial class MaterialService
{
    private async Task ValidateSourceAsync(MaterialDocument doc, MaterialActor actor, CancellationToken ct)
    {
        if (doc.ProcurementRequestId is not { } requestId) return;
        var record = (await requestRecords.GetByIdAsync(requestId, ct))!;
        var request = (await requests.GetAsync(record.DisplayCode, false, ct))!;
        if (request.RequiresReview || request.MappingIssue is not null || request.Status is ProcurementRequestStatus.Cancelled or ProcurementRequestStatus.Completed)
            throw MaterialErrors.Conflict("SOURCE_REQUEST_NOT_READY", "Resolve the source request before posting materials.");
        if (doc.Kind is not (MaterialDocumentKind.Receipt or MaterialDocumentKind.Issue)) throw MaterialErrors.Invalid("Purchase request links are supported on material receipt and issue documents.");
        if (request.RequestType != ProcurementRequestType.Purchase) throw MaterialErrors.Invalid("Repair material issues must link to the repair work task instead.");
        if (request.Lines.Any(l => l.Kind == ProcurementLineKind.Service)) throw MaterialErrors.Invalid("Standalone services need their own fulfillment evidence.");
        var requested = request.Lines.Where(l => l.Kind == ProcurementLineKind.Material).ToArray();
        if (requested.Length == 0) throw MaterialErrors.Invalid("The source request has no material lines.");
        foreach (var line in doc.Lines)
        {
            var source = requested.SingleOrDefault(l => l.SourceLineCode == Code(line.SourceLineCode)) ?? throw MaterialErrors.Invalid("Link each material to a material source line.");
            if (!string.Equals(source.Unit.Trim(), line.UnitCodeSnapshot, StringComparison.OrdinalIgnoreCase) && !string.Equals(source.Unit.Trim(), line.UnitNameSnapshot, StringComparison.OrdinalIgnoreCase))
                throw MaterialErrors.Invalid("Source and inventory units must match; convert or correct the source unit explicitly.");
            line.SourceLineCode = source.SourceLineCode;
        }
        var existing = (await store.RequestDocumentsAsync(requestId, ct)).Where(d => d.Id != doc.Id && d.Status is not (MaterialDocumentStatus.Cancelled or MaterialDocumentStatus.Rejected)).ToArray();
        if (existing.Any(d => d.Kind == doc.Kind)) throw MaterialErrors.Conflict("SOURCE_DOCUMENT_ALREADY_EXISTS", "A material document of this kind already exists for this request.");
        foreach (var source in requested)
        {
            var amount = doc.Lines.Where(l => l.SourceLineCode == source.SourceLineCode).Sum(l => l.Quantity);
            if (doc.Kind == MaterialDocumentKind.Issue ? amount != source.Quantity : amount > source.Quantity)
                throw MaterialErrors.Invalid("Issue every requested material in full; receipts may cover only missing stock.");
        }
        if (doc.Kind == MaterialDocumentKind.Issue)
        {
            var target = await uow.Departments.FirstOrDefaultAsync(d => d.Code == request.RequestingDepartmentCode, ct) ?? throw MaterialErrors.Missing();
            if (doc.TargetDepartmentId is not null && doc.TargetDepartmentId != target.Id) throw MaterialErrors.Invalid("Material issue must serve the requesting department.");
            doc.TargetDepartmentId = target.Id;
            var receipt = existing.SingleOrDefault(d => d.Kind == MaterialDocumentKind.Receipt);
            if (receipt is not null && receipt.Status != MaterialDocumentStatus.Completed) throw MaterialErrors.Conflict("SOURCE_RECEIPT_NOT_COMPLETED", "Receive materials into QTTB before allocation.");
            if (receipt is not null)
                foreach (var line in receipt.Lines)
                    if (doc.Lines.Where(l => l.SourceLineCode == line.SourceLineCode && l.MaterialId == line.MaterialId).Sum(l => l.Quantity) < line.Quantity)
                        throw MaterialErrors.Invalid("Allocate every material received for the source request.");
        }
        else if (existing.Any(d => d.Kind == MaterialDocumentKind.Issue)) throw MaterialErrors.Invalid("Prepare incoming materials before creating the issue.");
    }
    private async Task<bool> RequiresCombinedDeliveryAsync(MaterialDocument doc, CancellationToken ct)
    {
        if (doc.Kind != MaterialDocumentKind.Issue || doc.ProcurementRequestId is not { } requestId) return false;
        var record = await requestRecords.GetByIdAsync(requestId, ct);
        var request = record is null ? null : await requests.GetAsync(record.DisplayCode, false, ct);
        return request?.Lines.Any(l => l.Kind == ProcurementLineKind.Equipment) == true;
    }
    private async Task CompleteCombinedDeliveryAsync(MaterialDocument doc, MaterialAction input, MaterialActor actor, CancellationToken ct)
    {
        var detail = await DetailAsync(doc, actor, ct);
        if (detail.EquipmentRequirements.Count == 0)
        { if (input.Devices.Count > 0) throw MaterialErrors.Invalid("No equipment belongs to this material delivery."); return; }
        var requestId = doc.ProcurementRequestId!.Value;
        var record = (await requestRecords.GetByIdAsync(requestId, ct))!;
        var request = (await requests.GetAsync(record.DisplayCode, false, ct))!;
        if (request.RequiresReview || request.MappingIssue is not null || request.Status is ProcurementRequestStatus.Completed or ProcurementRequestStatus.Cancelled) throw MaterialErrors.Invalid("The source request is not ready for handover.");
        if (input.Devices.Count == 0 || input.Devices.Select(d => Code(d.DeviceCode)).Distinct().Count() != input.Devices.Count) throw MaterialErrors.Invalid("Confirm every equipment item in this combined delivery exactly once.");
        deliveryScope.RequestId = requestId;
        try
        {
            if (detail.CombinedInternalUse)
            {
                var responsible = doc.TargetResponsibleAccountId is { } id ? (await uow.Accounts.GetByIdAsync(id, ct))!.AccountCode : actor.AccountCode;
                await deviceService.PutIntoUseBatchAsync(new PutDeviceIntoUseBatchRequest { SourceRequestCode = request.DisplayCode,
                    Items = input.Devices.Select(d => new PutDeviceIntoUseItem { DeviceCode = d.DeviceCode, SourceLineCode = d.SourceLineCode ?? "", ResponsibleAccountCode = responsible, Notes = d.Note }).ToList() }, ct);
            }
            else
            {
                if (detail.CombinedTransferCode is null) throw MaterialErrors.Conflict("SOURCE_REQUEST_INCOMPLETE", "Prepare and approve the equipment allocation before confirming this delivery.");
                var transfer = (await uow.DeviceTransfers.FirstOrDefaultAsync(t => t.TransferCode == detail.CombinedTransferCode, ct))!;
                await transferService.HandoverCompleteAsync(transfer.TransferCode, new TransferHandoverRequest { Version = transfer.Version,
                    Items = input.Devices.Select(d => new TransferHandoverItemRequest { DeviceCode = d.DeviceCode, Condition = d.Condition, Note = d.Note }).ToList() }, ct);
            }
        }
        finally { deliveryScope.RequestId = null; }
    }
}
