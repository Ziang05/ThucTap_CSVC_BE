using CSVC.Core.Materials;
using CSVC.Core.ProcurementRequests;
using CSVC.UseCases.Common;

namespace CSVC.UseCases.Materials;

public sealed partial class MaterialService
{
    public async Task<PagedResult<MaterialDocumentSummary>> DocumentsAsync(MaterialQuery q, CancellationToken ct)
    { var actor = await access.RequireAsync(ct, allowDepartment: true); Query(q); return await store.DocumentsAsync(q, actor, ct); }
    public async Task<MaterialDocumentDetail> GetDocumentAsync(string code, CancellationToken ct)
    {
        var actor = await access.RequireAsync(ct, allowDepartment: true);
        return await DetailAsync(await ReadDocumentAsync(code, actor, false, ct), actor, ct);
    }
    private async Task<MaterialDocument> ReadDocumentAsync(string code, MaterialActor actor, bool tracked, CancellationToken ct)
    {
        var doc = await store.DocumentAsync(Code(code), tracked, ct) ?? throw MaterialErrors.Missing("Material document not found.");
        if (actor.Department ? doc.TargetDepartmentId != actor.DepartmentId : doc.DepartmentId != actor.DepartmentId) throw MaterialErrors.Missing();
        return doc;
    }
    public async Task<PagedResult<MaterialHistoryDto>> HistoryAsync(string code, int page, int pageSize, CancellationToken ct)
    {
        var actor = await access.RequireAsync(ct, allowDepartment: true); var doc = await ReadDocumentAsync(code, actor, false, ct);
        if (page < 1 || page > 1000000 || pageSize < 1 || pageSize > 100) throw MaterialErrors.Invalid("Invalid paging.");
        return await store.HistoryAsync(doc.Id, page, pageSize, ct);
    }
    private async Task<MaterialDocumentDetail> DetailAsync(MaterialDocument doc, MaterialActor actor, CancellationToken ct)
    {
        var actorIds = new[] { doc.ApprovedByAccountId, doc.ReceivedByAccountId, doc.TargetResponsibleAccountId }.Where(x => x is not null).Select(x => x!.Value).ToArray();
        var accounts = (await uow.Accounts.FindAsync(a => actorIds.Contains(a.Id), ct)).ToDictionary(a => a.Id, a => a.AccountCode);
        string? Account(Guid? id) => id is { } value ? accounts.GetValueOrDefault(value) : null;
        var supplier = doc.SupplierId is { } supplierId ? await uow.Suppliers.GetByIdAsync(supplierId, ct) : null;
        var service = doc.ServiceOrderId is { } serviceId ? await services.GetByIdAsync(serviceId, ct) : null;
        var materialIds = doc.Lines.Select(l => l.MaterialId).ToArray();
        var items = (await materials.FindAsync(m => materialIds.Contains(m.Id), ct)).ToDictionary(m => m.Id);
        var unitIds = items.Values.Select(m => m.UnitId).ToArray();
        var unitMap = (await units.FindAsync(u => unitIds.Contains(u.Id), ct)).ToDictionary(u => u.Id);
        var locationIds = doc.Lines.SelectMany(l => new Guid?[] { l.LocationId, l.ToLocationId }).Where(id => id is not null).Select(id => id!.Value).Distinct().ToArray();
        var locationMap = (await locations.FindAsync(l => locationIds.Contains(l.Id), ct)).ToDictionary(l => l.Id);
        var warehouseIds = locationMap.Values.Select(l => l.WarehouseId).Distinct().ToArray();
        var warehouseMap = (await warehouses.FindAsync(w => warehouseIds.Contains(w.Id), ct)).ToDictionary(w => w.Id);
        var deviceIds = doc.Lines.Where(l => l.DeviceId is not null).Select(l => l.DeviceId!.Value).ToArray();
        var deviceMap = (await uow.Devices.FindAsync(d => deviceIds.Contains(d.Id), ct)).ToDictionary(d => d.Id, d => d.DeviceCode);
        var lines = new List<MaterialLineDto>();
        foreach (var line in doc.Lines.OrderBy(l => l.LineNo))
        {
            var item = items[line.MaterialId]; var location = locationMap[line.LocationId];
            var used = doc.Kind == MaterialDocumentKind.Issue ? await store.UsedAsync(line.Id, null, ct) : 0;
            var returned = doc.Kind == MaterialDocumentKind.Issue ? await store.ReturnedAsync(line.Id, MaterialDocumentKind.ReturnUnused, ct) : 0;
            var recovered = doc.Kind == MaterialDocumentKind.Issue ? await store.ReturnedAsync(line.Id, MaterialDocumentKind.ReceiveRecovered, ct) : 0;
            lines.Add(new(line.Id, line.LineNo, line.MaterialCodeSnapshot, line.MaterialNameSnapshot, item.Kind,
                line.UnitCodeSnapshot, line.UnitNameSnapshot, unitMap[item.UnitId].DecimalPlaces,
                location.Code, location.Name, warehouseMap[location.WarehouseId].Code,
                line.ToLocationId is { } to ? locationMap[to].Code : null, line.Quantity, line.Condition, line.ConditionNote,
                line.DeviceId is { } device ? deviceMap.GetValueOrDefault(device) : null,
                line.OriginalIssueLineId, line.RemovedMaterialId, line.SourceLineCode, line.LotOrSerial, used, returned,
                item.Kind == MaterialKind.Tool ? Math.Max(0, used - recovered) : 0,
                doc.Kind == MaterialDocumentKind.Issue ? await PendingReturnAsync(line.Id, MaterialDocumentKind.ReturnUnused, null, ct) : 0,
                doc.Kind == MaterialDocumentKind.Issue ? await PendingReturnAsync(line.Id, MaterialDocumentKind.ReceiveRecovered, null, ct) : 0));
        }
        string? transferCode = null; var internalUse = false; var requirements = new List<MaterialDeliveryRequirement>();
        if (doc.Kind == MaterialDocumentKind.Issue && doc.ProcurementRequestId is { } requestId)
        {
            var record = await requestRecords.GetByIdAsync(requestId, ct);
            var request = record is null ? null : await requests.GetAsync(record.DisplayCode, false, ct);
            if (request is not null)
            {
                requirements.AddRange(request.Lines.Where(l => l.Kind == ProcurementLineKind.Equipment)
                    .Select(l => new MaterialDeliveryRequirement(l.SourceLineCode, l.Description, (int)l.Quantity, l.CategoryCode)));
                internalUse = requirements.Count > 0 && doc.TargetDepartmentId == doc.DepartmentId;
                foreach (var link in await requestLinks.FindAsync(l => l.RequestId == requestId && l.Kind == RequestDocumentKind.Allocation, ct))
                {
                    var allocation = await uow.DeviceTransfers.FirstOrDefaultAsync(t => t.TransferCode == link.DocumentCode, ct);
                    if (allocation is not null && allocation.Status is not (CSVC.Core.DeviceTransfers.DeviceTransferStatus.Cancelled or CSVC.Core.DeviceTransfers.DeviceTransferStatus.Rejected)) transferCode = allocation.TransferCode;
                }
            }
        }
        return new(await store.SummaryAsync(doc.Id, ct), doc.ReferenceDate, doc.Reference, doc.Notes, supplier?.DisplayCode, supplier?.Name,
            Account(doc.ApprovedByAccountId), doc.ApprovedAt, Account(doc.ReceivedByAccountId), doc.ReceivedAt,
            Account(doc.TargetResponsibleAccountId), service?.DisplayCode, transferCode, internalUse, requirements, lines,
            doc.Kind == MaterialDocumentKind.Issue && doc.Status == MaterialDocumentStatus.Completed && doc.PostedAt != null
                && doc.TargetDepartmentId == actor.DepartmentId && (actor.Staff || actor.Department),
            doc.Kind == MaterialDocumentKind.Issue && doc.Status == MaterialDocumentStatus.Completed && doc.PostedAt != null
                && doc.TargetDepartmentId == actor.DepartmentId && (actor.Staff || actor.Department),
            doc.Kind is MaterialDocumentKind.ReturnUnused or MaterialDocumentKind.ReceiveRecovered
                && doc.Status == MaterialDocumentStatus.PendingApproval && (actor.Staff || actor.Manager));
    }
    private async Task<EquipmentChangeDto> ChangeDtoAsync(EquipmentChange change, CancellationToken ct)
    {
        var device = (await uow.Devices.GetByIdAsync(change.DeviceId, ct))!;
        var service = change.ServiceOrderId is { } serviceId ? await services.GetByIdAsync(serviceId, ct) : null;
        var actor = (await uow.Accounts.GetByIdAsync(change.RecordedByAccountId, ct))!;
        var profile = (await uow.UserProfiles.GetByIdAsync(actor.UserProfileId, ct))!;
        var previous = change.ReplacesId is { } oldId ? await changes.GetByIdAsync(oldId, ct) : null;
        var replacement = await changes.FirstOrDefaultAsync(c => c.ReplacesId == change.Id, ct);
        var useItems = new List<EquipmentUseDto>(); var removedItems = new List<EquipmentRemovedDto>();
        foreach (var used in change.Uses)
            useItems.Add(new(used.Id, used.Source, used.IssueLineId, used.MaterialId is { } id ? (await materials.GetByIdAsync(id, ct))?.Code : null,
                used.Description, used.Unit, used.Quantity, used.LotOrSerial, used.SourceReference));
        foreach (var item in change.Removed)
        {
            var refs = await documentLines.FindAsync(l => l.RemovedMaterialId == item.Id, ct);
            var ids = refs.Select(l => l.DocumentId).ToArray();
            var recovery = (await documents.FindAsync(d => ids.Contains(d.Id) && d.Status != MaterialDocumentStatus.Cancelled && d.Status != MaterialDocumentStatus.Rejected, ct)).OrderByDescending(d => d.CreatedAt).FirstOrDefault();
            removedItems.Add(new(item.Id, item.MaterialId is { } id ? (await materials.GetByIdAsync(id, ct))?.Code : null,
                item.Description, item.Unit, item.Quantity, item.Reusable, item.ConditionNote, item.Disposition, item.RecipientOrEvidence, item.LotOrSerial, recovery?.Code));
        }
        return new(change.Code, device.DeviceCode, device.Name, service?.DisplayCode, previous?.Code, replacement?.Code,
            change.Kind, change.Target, change.BeforeValue, change.AfterValue, change.Reason, change.Notes,
            actor.AccountCode, profile.FullName, change.Executor, change.PerformedAt, change.RecordedAt, useItems, removedItems,
            change.IssueDocumentId is { } issueId ? (await documents.GetByIdAsync(issueId, ct))?.Code : null);
    }
}
