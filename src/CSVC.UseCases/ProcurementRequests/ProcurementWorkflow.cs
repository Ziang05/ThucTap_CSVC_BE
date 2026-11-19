using System.Text.Json;
using System.Text.Json.Serialization;
using CSVC.Core.Common;
using CSVC.Core.DeviceReceipts;
using CSVC.Core.DeviceTransfers;
using CSVC.Core.ProcurementRequests;
using CSVC.Core.ServiceOrders;
using CSVC.UseCases.Abstractions.Persistence;
using CSVC.UseCases.Exceptions;
using CSVC.UseCases.Settings;

namespace CSVC.UseCases.ProcurementRequests;

public static class ProcurementJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Read<T>(string value) => JsonSerializer.Deserialize<T>(value, Options)!;
}

public sealed class ProcurementWorkflow(IProcurementRequestRepository requests, IRepository<ProcurementDocumentLink> links,
    IRepository<ProcurementRequestEvent> events, IUnitOfWork uow, IServiceOrderRepository services,
    IEquipmentDepartmentPolicy equipment, TimeProvider clock,
    CSVC.UseCases.Materials.IMaterialWorkflow materialWorkflow, CSVC.UseCases.Materials.MaterialDeliveryScope deliveryScope,
    IRepository<ProcurementRequest> requestRecords) : IProcurementWorkflow
{
    public async Task AttachAsync(string? sourceCode, RequestDocumentKind kind, string documentCode, Guid departmentId,
        IReadOnlyList<RequestDeviceBinding> inputs, Guid actorId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sourceCode))
        {
            if (inputs.Any(i => !string.IsNullOrWhiteSpace(i.SourceLineCode))) throw Invalid("Source line codes require a sourceRequestCode.");
            return;
        }
        var request = await requests.GetAsync(sourceCode, true, ct) ?? throw new UseCaseException(FailureKind.NotFound, "SOURCE_REQUEST_NOT_FOUND", "The source request does not exist.");
        if (request.Status is ProcurementRequestStatus.Completed or ProcurementRequestStatus.Cancelled || request.RequiresReview || request.MappingIssue is not null)
            throw Conflict("SOURCE_REQUEST_NOT_READY", "Resolve the source request before creating documents.");
        if (!await uow.Departments.ExistsAsync(d => (kind == RequestDocumentKind.Repair || d.Id == departmentId) && d.Code == request.RequestingDepartmentCode && d.Status == EntityStatus.Active, ct))
            throw Invalid("The document must serve the source requesting department.");
        if ((kind == RequestDocumentKind.Repair) != (request.RequestType == ProcurementRequestType.Repair)) throw Invalid("Document type does not match the source request.");
        if (kind == RequestDocumentKind.InternalUse && request.Lines.Any(l => l.Kind == ProcurementLineKind.Material) && deliveryScope.RequestId != request.Id)
            throw Conflict("COMBINED_DELIVERY_REQUIRED", "Confirm mixed equipment and materials from the material issue receipt operation.");
        var documents = await DocumentsAsync(request, ct);
        if (documents.Any(d => d.Kind == kind && (d.Open || d.Executed))) throw Conflict("SOURCE_DOCUMENT_ALREADY_EXISTS", "Finish the existing document or cancel its draft before creating a replacement.");
        var bindings = inputs.Select(i => i with { SourceLineCode = BusinessCode.Normalize(i.SourceLineCode) ?? "", DeviceCode = BusinessCode.Normalize(i.DeviceCode) ?? "", CategoryCode = BusinessCode.Normalize(i.CategoryCode), DamageReportCode = BusinessCode.Normalize(i.DamageReportCode) }).ToArray();
        if (bindings.Length == 0 || bindings.Select(b => b.DeviceCode).Distinct().Count() != bindings.Length) throw Invalid("Each device must be linked exactly once.");
        foreach (var binding in bindings)
        {
            var line = request.Lines.SingleOrDefault(l => l.SourceLineCode == binding.SourceLineCode) ?? throw Invalid("Every item needs a valid sourceLineCode.");
            if (kind == RequestDocumentKind.Repair && line.Specialization == EquipmentSpecialization.Specialized && binding.ExecutionMethod == "OnSite")
            {
                if (string.IsNullOrWhiteSpace(binding.SpecializedHandlingReason))
                    throw Invalid("Specialized equipment defaults to external handling. Record the on-site exception reason for manager approval.");
            }
            if (line.Kind != ProcurementLineKind.Equipment) throw Invalid("Equipment documents cannot represent consumable materials or standalone services.");
            if (kind == RequestDocumentKind.Repair && line.DeviceCode != binding.DeviceCode) throw Invalid("Repair must reference the source device.");
            if (kind == RequestDocumentKind.Repair && line.DamageReportCode is not null && line.DamageReportCode != binding.DamageReportCode) throw Invalid("Retain the damage report referenced by the source repair line.");
            if (line.CategoryCode is not null && binding.CategoryCode is not null && line.CategoryCode != binding.CategoryCode) throw Invalid("Device category does not match the source line.");
        }
        foreach (var line in request.Lines.Where(l => l.Kind == ProcurementLineKind.Equipment))
        {
            var count = bindings.Count(b => b.SourceLineCode == line.SourceLineCode);
            if (kind == RequestDocumentKind.Receipt ? count > line.Quantity : count != line.Quantity)
                throw Invalid("The receipt may cover the missing stock; allocation and repair must cover the entire request at once.");
        }
        if (kind == RequestDocumentKind.Receipt && documents.Any(d => (d.Kind == RequestDocumentKind.Allocation || d.Kind == RequestDocumentKind.InternalUse) && (d.Open || d.Executed)))
            throw Conflict("SOURCE_ALLOCATION_ALREADY_STARTED", "Prepare all incoming goods before creating the allocation.");
        if (kind is RequestDocumentKind.Allocation or RequestDocumentKind.InternalUse)
        {
            if (documents.Any(d => d.Kind is RequestDocumentKind.Allocation or RequestDocumentKind.InternalUse && (d.Open || d.Executed)))
                throw Conflict("SOURCE_DOCUMENT_ALREADY_EXISTS", "The request already has an allocation or internal-use record.");
            var receipt = documents.SingleOrDefault(d => d.Kind == RequestDocumentKind.Receipt && (d.Open || d.Executed));
            if (receipt is { Open: true }) throw Conflict("SOURCE_RECEIPT_NOT_COMPLETED", "Receive the purchase into QTTB first.");
            if (receipt is not null && receipt.Bindings.Any(b => !bindings.Any(i => i.DeviceCode == b.DeviceCode && i.SourceLineCode == b.SourceLineCode)))
                throw Invalid("Allocate every device received for this source request.");
            var equipmentId = await equipment.RequireConfiguredAsync(ct);
            var codes = bindings.Select(b => b.DeviceCode).ToArray();
            var selected = await uow.Devices.FindAsync(d => codes.Contains(d.DeviceCode), ct);
            if (selected.Count != codes.Length || selected.Any(d => d.DepartmentId != equipmentId || d.Quantity != 1)) throw Invalid("Allocate only received devices currently held by QTTB.");
        }
        await links.AddAsync(new ProcurementDocumentLink { RequestId = request.Id, Kind = kind, DocumentCode = documentCode,
            BindingsJson = ProcurementJson.Write(bindings), LinkedByAccountId = actorId, LinkedAt = clock.GetUtcNow() }, ct);
        request.Version++;
        if (request.Status == ProcurementRequestStatus.Received) request.Status = ProcurementRequestStatus.InProgress;
        await events.AddAsync(new ProcurementRequestEvent { RequestId = request.Id, Action = "DOCUMENT_LINKED", Outcome = kind.ToString(),
            Reason = documentCode, ActorAccountId = actorId, OccurredAt = clock.GetUtcNow() }, ct);
    }

    public async Task PreventLinkedEditAsync(RequestDocumentKind kind, string documentCode, CancellationToken ct)
    {
        if (await links.ExistsAsync(l => l.Kind == kind && l.DocumentCode == documentCode, ct))
            throw Conflict("SOURCE_DOCUMENT_EDIT_FORBIDDEN", "Cancel the draft and create its replacement from the source request to preserve line bindings.");
    }

    public async Task<IReadOnlyList<RequestDocumentDto>> DocumentsAsync(ProcurementRequest request, CancellationToken ct)
    {
        var result = new List<RequestDocumentDto>();
        foreach (var link in await links.FindAsync(l => l.RequestId == request.Id, ct))
        {
            string status; bool executed, open;
            if (link.Kind == RequestDocumentKind.Receipt)
            {
                var document = await uow.DeviceReceipts.FirstOrDefaultAsync(r => r.ReceiptCode == link.DocumentCode, ct) ?? throw Conflict("SOURCE_DOCUMENT_MISSING", "A linked receipt is missing.");
                status = document.Status.ToString(); executed = document.Status == DeviceReceiptStatus.Completed;
                open = document.Status is DeviceReceiptStatus.PendingApproval or DeviceReceiptStatus.Approved;
            }
            else if (link.Kind == RequestDocumentKind.Allocation)
            {
                var document = await uow.DeviceTransfers.FirstOrDefaultAsync(r => r.TransferCode == link.DocumentCode, ct) ?? throw Conflict("SOURCE_DOCUMENT_MISSING", "A linked allocation is missing.");
                status = document.Status.ToString(); executed = document.Status == DeviceTransferStatus.Completed;
                open = document.Status is DeviceTransferStatus.PendingConfirmation or DeviceTransferStatus.PendingApproval or DeviceTransferStatus.Approved;
            }
            else if (link.Kind == RequestDocumentKind.InternalUse)
            {
                status = "Completed"; executed = true; open = false;
            }
            else
            {
                var document = await services.GetAggregateAsync(link.DocumentCode, false, ct) ?? throw Conflict("SOURCE_DOCUMENT_MISSING", "A linked repair is missing.");
                status = document.Status.ToString(); executed = document.Status is ServiceWorkflowStatus.InProgress or ServiceWorkflowStatus.Completed;
                open = document.Status is ServiceWorkflowStatus.PendingApproval or ServiceWorkflowStatus.InProgress;
            }
            result.Add(new(link.Kind, link.DocumentCode, status, executed, open, ProcurementJson.Read<RequestDeviceBinding[]>(link.BindingsJson)));
        }
        if (request.ServiceEvidenceJson is not null)
            result.Add(new(RequestDocumentKind.ServiceEvidence, request.DisplayCode + "-DV", "Recorded", true, false, []));
        result.AddRange(await materialWorkflow.RequestDocumentsAsync(request, ct));
        return result;
    }
    public async Task RequireCombinedDeliveryAsync(string allocationCode, CancellationToken ct)
    {
        var link = await links.FirstOrDefaultAsync(l => l.Kind == RequestDocumentKind.Allocation && l.DocumentCode == allocationCode, ct);
        if (link is null) return;
        var record = (await requestRecords.GetByIdAsync(link.RequestId, ct))!;
        var request = (await requests.GetAsync(record.DisplayCode, false, ct))!;
        if (request.Lines.Any(l => l.Kind == ProcurementLineKind.Material) && deliveryScope.RequestId != link.RequestId) throw Conflict("COMBINED_DELIVERY_REQUIRED", "Receive equipment and material together from the linked material issue.");
    }
    private static UseCaseException Invalid(string reason) => new(FailureKind.Validation, "SOURCE_DOCUMENT_INVALID", reason);
    private static UseCaseException Conflict(string code, string reason) => new(FailureKind.Conflict, code, reason);
}
