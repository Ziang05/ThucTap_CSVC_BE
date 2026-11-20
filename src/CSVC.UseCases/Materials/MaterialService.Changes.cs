using CSVC.Core.Common;
using CSVC.Core.Materials;
using CSVC.Core.ServiceOrders;
using CSVC.UseCases.Common;

namespace CSVC.UseCases.Materials;

public sealed partial class MaterialService
{
    public async Task<PagedResult<EquipmentChangeDto>> ChangesAsync(string? deviceCode, MaterialQuery q, CancellationToken ct)
    {
        var actor = await access.RequireAsync(ct, allowDepartment: true); Query(q);
        Guid? deviceId = null;
        if (Clean(deviceCode) is not null)
        {
            var device = await uow.Devices.FirstOrDefaultAsync(d => d.DeviceCode == Code(deviceCode), ct) ?? throw MaterialErrors.Missing("Device not found.");
            if (actor.Department && device.DepartmentId != actor.DepartmentId) throw MaterialErrors.Missing(); deviceId = device.Id;
        }
        else if (actor.Department) throw MaterialErrors.Forbidden();
        var result = await store.ChangesAsync(deviceId, q, ct); var rows = new List<EquipmentChangeDto>();
        foreach (var change in result.Items) rows.Add(await ChangeDtoAsync(change, ct));
        return new(rows, result.TotalCount, result.Page, result.PageSize);
    }
    private async Task<(MaterialDocument Document, MaterialDocumentLine Line, MaterialItem Item, MaterialUnit Unit)> IssueForUseAsync(Guid lineId, Guid? serviceId, Guid? deviceId, CancellationToken ct)
    {
        var doc = await store.IssueOfAsync(lineId, ct) ?? throw MaterialErrors.Missing("Issued material not found.");
        var line = doc.Lines.Single(l => l.Id == lineId);
        if (doc.Kind != MaterialDocumentKind.Issue || doc.Status != MaterialDocumentStatus.Completed || doc.ServiceOrderId != serviceId || line.DeviceId != deviceId)
            throw MaterialErrors.Invalid("Materials must be received from an issue for this work task and equipment.");
        var item = (await materials.GetByIdAsync(line.MaterialId, ct))!; var unit = (await units.GetByIdAsync(item.UnitId, ct))!;
        return (doc, line, item, unit);
    }
    public async Task<EquipmentChangeDto> RecordChangeAsync(RecordEquipmentChange input, CancellationToken ct)
    {
        var code = await transaction.ExecuteAsync(async token =>
        {
            var actor = await access.RequireAsync(token); Validate(input); RequestId(input.RequestId); var hash = Hash(input);
            if (input.Uses.Any(u => u is null) || input.Removed.Any(r => r is null)) throw MaterialErrors.Invalid("Material usage and removal rows cannot be null.");
            var prior = await changes.FirstOrDefaultAsync(c => c.RequestId == input.RequestId, token);
            if (prior is not null)
            { if (prior.RecordedByAccountId != actor.Id || prior.Fingerprint != hash) throw MaterialErrors.Conflict("IDEMPOTENCY_CONFLICT", "requestId already used."); return prior.Code; }
            RequireRecorder(actor);
            if (input.Kind == EquipmentChangeKind.WorkResult) throw MaterialErrors.Invalid("Record professional results on the service order; select a detailed intervention type here.");
            var device = await uow.Devices.FirstOrDefaultAsync(d => d.DeviceCode == Code(input.DeviceCode) && d.Quantity == 1 && d.Status == EntityStatus.Active, token) ?? throw MaterialErrors.Missing("Received active device not found.");
            await transaction.LockDevicesAsync([device.Id], token);
            ServiceOrder? service = null;
            if (Clean(input.ServiceOrderCode) is not null)
            {
                service = await services.GetAggregateAsync(Code(input.ServiceOrderCode), false, token) ?? throw MaterialErrors.Missing();
                await assignment.RequireDocumentAsync(CSVC.Core.ProcurementRequests.RequestDocumentKind.Repair, service.DisplayCode, actor.Id, token);
                if (service.Status != ServiceWorkflowStatus.InProgress || !service.Items.Any(i => i.DeviceId == device.Id && i.ExecutionStatus == ServiceItemStatus.InProgress)) throw MaterialErrors.Invalid("The device must be in progress on this service order.");
                if (!await serviceHolds.ExistsAsync(h => h.DeviceId == device.Id && h.ServiceOrderId == service.Id, token)) throw MaterialErrors.Conflict("DEVICE_BUSY", "The repair no longer holds this equipment.");
                var guard = await exportGuards.FirstOrDefaultAsync(g => g.DeviceId == device.Id, token);
                if (guard?.GuardState == ExportGuardState.HeldForExport) throw MaterialErrors.Conflict("EXPORT_RESULT_PENDING", "Confirm the export result before recording the actual intervention.");
                if (service.Items.Single(i => i.DeviceId == device.Id).ExecutionMethod == ExecutionMethod.External)
                {
                    if (!service.ExportSlips.Any(e => e.Items.Any(l => l.ServiceOrderItemId == service.Items.Single(i => i.DeviceId == device.Id).Id && l.Result == ExportResult.Success))) throw MaterialErrors.Invalid("External interventions require a successful export.");
                    if (Clean(input.Executor) is null) throw MaterialErrors.Invalid("Identify the actual person or provider that performed the external work.");
                }
            }
            else
            {
                if (device.DepartmentId != actor.DepartmentId) throw MaterialErrors.Invalid("Equipment changes require a service order or a direct intervention for equipment in its custody.");
                await reservations.RequireAsync(device, false, ct: token);
            }
            if (Clean(input.BeforeValue) is null && Clean(input.AfterValue) is null) throw MaterialErrors.Invalid("Record the state before or after the intervention.");
            var now = clock.GetUtcNow(); var performed = input.PerformedAt ?? now;
            if (performed > now || performed < (service?.CreatedAt ?? DateTimeOffset.MinValue)) throw MaterialErrors.Invalid("The intervention time must be between service creation and the present.");
            EquipmentChange? replaced = null;
            if (Clean(input.ReplacesCode) is not null)
            {
                replaced = await store.ChangeAsync(Code(input.ReplacesCode), token) ?? throw MaterialErrors.Missing("Previous change not found.");
                if (replaced.DeviceId != device.Id || replaced.ServiceOrderId != service?.Id || await changes.ExistsAsync(c => c.ReplacesId == replaced.Id, token)) throw MaterialErrors.Invalid("Only the latest change on the same intervention may be corrected.");
                var removalIds = replaced.Removed.Select(r => r.Id).ToArray();
                var refs = await documentLines.FindAsync(l => l.RemovedMaterialId != null && removalIds.Contains(l.RemovedMaterialId.Value), token);
                var docIds = refs.Select(l => l.DocumentId).ToArray();
                if (await documents.ExistsAsync(d => docIds.Contains(d.Id) && d.Status != MaterialDocumentStatus.Cancelled && d.Status != MaterialDocumentStatus.Rejected, token)) throw MaterialErrors.Invalid("A removal referenced by a recovery document cannot be rewritten.");
                foreach (var oldUse in replaced.Uses.Where(u => u.IssueLineId != null))
                    if (await store.ReturnedAsync(oldUse.IssueLineId!.Value, MaterialDocumentKind.ReceiveRecovered, token) > 0) throw MaterialErrors.Invalid("Consumption already linked to a recovered tool return cannot be rewritten.");
            }
            var change = new EquipmentChange { Code = CSVC.UseCases.Common.FeatureCode.New("TD"), DeviceId = device.Id, ServiceOrderId = service?.Id,
                ReplacesId = replaced?.Id, RequestId = input.RequestId, Fingerprint = hash, Kind = input.Kind, Target = Name(input.Target),
                BeforeValue = Clean(input.BeforeValue), AfterValue = Clean(input.AfterValue), Reason = Name(input.Reason), Notes = Clean(input.Notes),
                RecordedByAccountId = actor.Id, Executor = Clean(input.Executor) ?? actor.FullName, PerformedAt = performed, RecordedAt = now };
            var grouped = input.Uses.Where(u => u.Source == MaterialUseSource.Warehouse).GroupBy(u => u.IssueLineId).ToArray();
            foreach (var group in grouped)
            {
                if (group.Key is not { } lineId) throw MaterialErrors.Invalid("Warehouse materials need an issue line.");
                var stock = await IssueForUseAsync(lineId, service?.Id, device.Id, token);
                Quantity(group.Sum(u => u.Quantity), stock.Unit.DecimalPlaces);
                if (group.Sum(u => u.Quantity) + await store.UsedAsync(lineId, replaced?.Id, token) + await store.ReturnedAsync(lineId, MaterialDocumentKind.ReturnUnused, token) > stock.Line.Quantity)
                    throw MaterialErrors.Conflict("MATERIAL_USE_EXCEEDS_ISSUE", "Used quantity exceeds issued quantity after returns.");
            }
            foreach (var used in input.Uses)
            {
                Validate(used); Quantity(used.Quantity, 3); MaterialItem? item = null; string description; string unit;
                if (used.Source == MaterialUseSource.Warehouse)
                { var stock = await IssueForUseAsync(used.IssueLineId!.Value, service?.Id, device.Id, token); item = stock.Item; Quantity(used.Quantity, stock.Unit.DecimalPlaces); description = stock.Line.MaterialNameSnapshot; unit = stock.Line.UnitNameSnapshot; }
                else
                {
                    if (used.IssueLineId is not null || Clean(used.Description) is null || Clean(used.Unit) is null || Clean(used.SourceReference) is null) throw MaterialErrors.Invalid("External materials require description, unit and a source reference; do not specify a warehouse issue.");
                    description = used.Description!.Trim(); unit = used.Unit!.Trim();
                    if (Clean(used.MaterialCode) is not null) item = await materials.FirstOrDefaultAsync(m => m.Code == Code(used.MaterialCode), token) ?? throw MaterialErrors.Missing();
                    if (item is not null) Quantity(used.Quantity, (await units.GetByIdAsync(item.UnitId, token))!.DecimalPlaces);
                }
                change.Uses.Add(new EquipmentMaterialUse { ChangeId = change.Id, Source = used.Source, IssueLineId = used.IssueLineId, MaterialId = item?.Id,
                    Description = description, Unit = unit, Quantity = used.Quantity, LotOrSerial = Clean(used.LotOrSerial), SourceReference = Clean(used.SourceReference) });
            }
            foreach (var part in input.Removed)
            {
                Validate(part); Quantity(part.Quantity, 3);
                var item = Clean(part.MaterialCode) is null ? null : await materials.FirstOrDefaultAsync(m => m.Code == Code(part.MaterialCode), token) ?? throw MaterialErrors.Missing();
                if (part.Disposition == RemovedDisposition.ReturnToStock)
                {
                    if (!part.Reusable || item is null) throw MaterialErrors.Invalid("Stock returns need a reusable item and material catalog entry.");
                    var unit = (await units.GetByIdAsync(item.UnitId, token))!; Quantity(part.Quantity, unit.DecimalPlaces);
                    if (!string.Equals(part.Unit.Trim(), unit.Name, StringComparison.OrdinalIgnoreCase) && !string.Equals(part.Unit.Trim(), unit.Code, StringComparison.OrdinalIgnoreCase)) throw MaterialErrors.Invalid("Recovered material unit must match its catalog.");
                }
                if (part.Disposition is RemovedDisposition.ReturnToDepartment or RemovedDisposition.Dispose && Clean(part.RecipientOrEvidence) is null) throw MaterialErrors.Invalid("Record the recipient or disposal evidence.");
                change.Removed.Add(new EquipmentRemovedMaterial { ChangeId = change.Id, MaterialId = item?.Id, Description = Name(part.Description), Unit = Name(part.Unit),
                    Quantity = part.Quantity, Reusable = part.Reusable, ConditionNote = Name(part.ConditionNote), Disposition = part.Disposition,
                    RecipientOrEvidence = Clean(part.RecipientOrEvidence), LotOrSerial = Clean(part.LotOrSerial) });
            }
            await changes.AddAsync(change, token); return change.Code;
        }, ct);
        return await ChangeDtoAsync((await store.ChangeAsync(code, ct))!, ct);
    }
    public async Task<MaterialWorkUseDto> RecordWorkUseAsync(RecordMaterialWorkUse input, CancellationToken ct)
    {
        var id = await transaction.ExecuteAsync(async token =>
        {
            var actor = await access.RequireAsync(token); Validate(input); RequestId(input.RequestId); var hash = Hash(input);
            var prior = await workUses.FirstOrDefaultAsync(u => u.RequestId == input.RequestId, token);
            if (prior is not null) { if (prior.ActorAccountId != actor.Id || prior.Fingerprint != hash) throw MaterialErrors.Conflict("IDEMPOTENCY_CONFLICT", "requestId already used."); return prior.Id; }
            RequireRecorder(actor);
            var sourceDoc = await store.IssueOfAsync(input.IssueLineId, token) ?? throw MaterialErrors.Missing();
            await assignment.RequireMaterialAsync(sourceDoc, actor.Id, token);
            if (sourceDoc.DepartmentId != actor.DepartmentId) throw MaterialErrors.Missing();
            var stock = await IssueForUseAsync(input.IssueLineId, sourceDoc.ServiceOrderId, null, token);
            if (sourceDoc.ServiceOrderId is { } serviceId && (await services.GetByIdAsync(serviceId, token))?.Status != ServiceWorkflowStatus.InProgress) throw MaterialErrors.Invalid("The service order is closed.");
            Quantity(input.Quantity, stock.Unit.DecimalPlaces);
            MaterialWorkUse? previous = null;
            if (Clean(input.ReplacesCode) is not null)
            {
                previous = await workUses.FirstOrDefaultAsync(u => u.Code == Code(input.ReplacesCode), token) ?? throw MaterialErrors.Missing();
                if (previous.IssueLineId != input.IssueLineId || await workUses.ExistsAsync(u => u.ReplacesId == previous.Id, token)) throw MaterialErrors.Invalid("Only the latest consumption of this work and issue line can be corrected.");
                if (await store.ReturnedAsync(input.IssueLineId, MaterialDocumentKind.ReceiveRecovered, token) > 0) throw MaterialErrors.Invalid("Consumption already linked to a recovered tool return cannot be corrected.");
            }
            if (input.Quantity + await store.UsedAsync(input.IssueLineId, null, token) - (previous?.Quantity ?? 0) + await store.ReturnedAsync(input.IssueLineId, MaterialDocumentKind.ReturnUnused, token) > stock.Line.Quantity)
                throw MaterialErrors.Conflict("MATERIAL_USE_EXCEEDS_ISSUE", "Used quantity exceeds outstanding issue.");
            var entity = new MaterialWorkUse { Code = CSVC.UseCases.Common.FeatureCode.New("SD"), IssueLineId = input.IssueLineId,
                ReplacesId = previous?.Id, RequestId = input.RequestId, Fingerprint = hash, Quantity = input.Quantity, Reason = Name(input.Reason), ActorAccountId = actor.Id, OccurredAt = clock.GetUtcNow() };
            await workUses.AddAsync(entity, token); return entity.Id;
        }, ct);
        return await WorkUseDtoAsync((await workUses.GetByIdAsync(id, ct))!, ct);
    }
    private async Task<MaterialWorkUseDto> WorkUseDtoAsync(MaterialWorkUse item, CancellationToken ct)
    {
        var line = (await documentLines.GetByIdAsync(item.IssueLineId, ct))!;
        return new(item.Code, item.IssueLineId, line.MaterialCodeSnapshot, line.MaterialNameSnapshot, line.UnitNameSnapshot,
            item.Quantity, item.Reason, (await uow.Accounts.GetByIdAsync(item.ActorAccountId, ct))!.AccountCode, item.OccurredAt,
            item.ReplacesId is { } old ? (await workUses.GetByIdAsync(old, ct))?.Code : null,
            (await workUses.FirstOrDefaultAsync(u => u.ReplacesId == item.Id, ct))?.Code);
    }
    public async Task<PagedResult<MaterialWorkUseDto>> WorkUsesAsync(MaterialQuery q, CancellationToken ct)
    {
        await access.RequireAsync(ct); Query(q);
        if (Clean(q.DocumentCode) is null) throw MaterialErrors.Invalid("Select an issue document to view consumption.");
        var doc = await store.DocumentAsync(Code(q.DocumentCode), false, ct) ?? throw MaterialErrors.Missing();
        var lineIds = doc.Lines.Select(l => l.Id).ToArray();
        var rows = (await workUses.FindAsync(u => lineIds.Contains(u.IssueLineId), ct)).OrderByDescending(u => u.OccurredAt).ThenBy(u => u.Code).ToArray();
        var results = new List<MaterialWorkUseDto>(); foreach (var row in rows.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize)) results.Add(await WorkUseDtoAsync(row, ct));
        return new(results, rows.Length, q.Page, q.PageSize);
    }
}
