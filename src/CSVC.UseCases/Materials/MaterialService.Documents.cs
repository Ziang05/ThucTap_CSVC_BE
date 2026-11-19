using CSVC.Core.Common;
using CSVC.Core.Materials;
using CSVC.Core.ServiceOrders;

namespace CSVC.UseCases.Materials;

public sealed partial class MaterialService
{
    public async Task<MaterialDocumentDetail> CreateDocumentAsync(CreateMaterialDocument input, CancellationToken ct)
    {
        var code = await transaction.ExecuteAsync(async token =>
        {
            var actor = await access.RequireAsync(token); Validate(input); RequestId(input.RequestId);
            var hash = Hash(input);
            var prior = await documents.FirstOrDefaultAsync(d => d.CreateRequestId == input.RequestId, token);
            if (prior is not null)
            {
                if (prior.CreatedByAccountId != actor.Id || prior.CreateHash != hash) throw MaterialErrors.Conflict("IDEMPOTENCY_CONFLICT", "requestId already belongs to another operation.");
                return prior.Code;
            }
            var doc = new MaterialDocument { Code = CSVC.UseCases.Common.FeatureCode.New("PV"), Kind = input.Kind,
                Status = input.Kind == MaterialDocumentKind.Adjustment ? MaterialDocumentStatus.PendingApproval : MaterialDocumentStatus.Ready,
                DepartmentId = actor.DepartmentId, CreateRequestId = input.RequestId, CreateHash = hash,
                CreatedByAccountId = actor.Id, CreatedAt = clock.GetUtcNow(), Reason = Name(input.Reason) };
            await FillDocumentAsync(doc, input, actor, token);
            await documents.AddAsync(doc, token);
            await DocumentAuditAsync(doc, actor, "CREATED", null, null, null, token);
            return doc.Code;
        }, ct);
        return await GetDocumentAsync(code, ct);
    }
    private async Task FillDocumentAsync(MaterialDocument doc, CreateMaterialDocument input, MaterialActor actor, CancellationToken ct)
    {
        if (input.Lines is null || input.Lines.Count is < 1 or > 200) throw MaterialErrors.Invalid("Supply 1 to 200 document lines.");
        doc.Reason = Name(input.Reason); doc.Notes = Clean(input.Notes); doc.ReferenceDate = input.ReferenceDate; doc.Reference = Clean(input.Reference);
        doc.SupplierId = Clean(input.SupplierCode) is null ? null : (await uow.Suppliers.FirstOrDefaultAsync(s => s.DisplayCode == Code(input.SupplierCode) && s.Status == EntityStatus.Active, ct) ?? throw MaterialErrors.Missing("Active supplier not found.")).Id;
        ServiceOrder? service = null;
        if (Clean(input.ServiceOrderCode) is not null)
        {
            service = await services.GetAggregateAsync(Code(input.ServiceOrderCode), false, ct) ?? throw MaterialErrors.Missing("Service order not found.");
            if (service.Status != ServiceWorkflowStatus.InProgress) throw MaterialErrors.Invalid("Service order must be in progress.");
        }
        doc.ServiceOrderId = service?.Id;
        doc.ProcurementRequestId = null;
        if (Clean(input.RequestCode) is not null)
        {
            var source = await requests.GetAsync(Code(input.RequestCode), false, ct) ?? throw MaterialErrors.Missing("Procurement request not found.");
            if (source.Status is CSVC.Core.ProcurementRequests.ProcurementRequestStatus.Completed or CSVC.Core.ProcurementRequests.ProcurementRequestStatus.Cancelled) throw MaterialErrors.Invalid("The source request is closed.");
            doc.ProcurementRequestId = source.Id;
        }
        doc.TargetDepartmentId = Clean(input.TargetDepartmentCode) is null ? (doc.Kind == MaterialDocumentKind.Issue && doc.ProcurementRequestId is null ? actor.DepartmentId : null)
            : (await uow.Departments.FirstOrDefaultAsync(d => d.Code == Code(input.TargetDepartmentCode) && d.Status == EntityStatus.Active, ct) ?? throw MaterialErrors.Missing("Active target department not found.")).Id;
        doc.TargetResponsibleAccountId = null;
        if (Clean(input.TargetResponsibleAccountCode) is not null)
        {
            var account = await uow.Accounts.FirstOrDefaultAsync(a => a.AccountCode == Code(input.TargetResponsibleAccountCode), ct) ?? throw MaterialErrors.Missing("Custodian not found.");
            var profile = await uow.UserProfiles.GetByIdAsync(account.UserProfileId, ct);
            if (account.IsLock || profile?.DepartmentId != doc.TargetDepartmentId) throw MaterialErrors.Invalid("Custodian must be active and belong to the receiving department.");
            doc.TargetResponsibleAccountId = account.Id;
        }
        if (doc.Kind == MaterialDocumentKind.Issue && doc.TargetDepartmentId is null && doc.ProcurementRequestId is null) throw MaterialErrors.Invalid("Select a receiving department.");
        if (doc.Kind == MaterialDocumentKind.Receipt && doc.SupplierId is null && doc.ProcurementRequestId is null && doc.Reference is null) throw MaterialErrors.Invalid("A receipt must identify its supplier, source request or source reference.");
        var index = 0; Guid? originalId = null;
        foreach (var line in input.Lines)
        {
            if (line is null) throw MaterialErrors.Invalid("Document lines cannot be null.");
            Validate(line);
            var item = await materials.FirstOrDefaultAsync(m => m.Code == Code(line.MaterialCode) && m.Status == EntityStatus.Active, ct) ?? throw MaterialErrors.Missing("Active material not found.");
            var unit = await units.GetByIdAsync(item.UnitId, ct) ?? throw MaterialErrors.Missing();
            if (unit.Status != EntityStatus.Active) throw MaterialErrors.Invalid("The unit is locked.");
            Quantity(line.Quantity, unit.DecimalPlaces, doc.Kind == MaterialDocumentKind.Adjustment);
            var location = await locations.FirstOrDefaultAsync(l => l.Code == Code(line.LocationCode), ct) ?? throw MaterialErrors.Missing("Location not found.");
            await ActiveLocationAsync(location.Id, actor.DepartmentId, ct);
            Guid? toId = null;
            if (doc.Kind == MaterialDocumentKind.Transfer)
            {
                var to = await locations.FirstOrDefaultAsync(l => l.Code == Code(line.ToLocationCode), ct) ?? throw MaterialErrors.Missing("Destination location not found.");
                await ActiveLocationAsync(to.Id, actor.DepartmentId, ct);
                if (to.Id == location.Id) throw MaterialErrors.Invalid("Transfer locations must differ."); toId = to.Id;
            }
            else if (Clean(line.ToLocationCode) is not null) throw MaterialErrors.Invalid("Only transfers have a destination location.");
            var device = Clean(line.DeviceCode) is null ? null : await uow.Devices.FirstOrDefaultAsync(d => d.DeviceCode == Code(line.DeviceCode) && d.Quantity == 1 && d.Status == EntityStatus.Active, ct) ?? throw MaterialErrors.Missing("Received active device not found.");
            if (service is not null && (device is null || !service.Items.Any(i => i.DeviceId == device.Id && i.ExecutionStatus == ServiceItemStatus.InProgress))) throw MaterialErrors.Invalid("Select an in-progress device from the service order.");
            if (device is not null && service is null && device.DepartmentId != actor.DepartmentId && device.DepartmentId != doc.TargetDepartmentId) throw MaterialErrors.Forbidden("The equipment is outside this document's scope.");
            if (line.Condition == MaterialCondition.Reusable && Clean(line.ConditionNote) is null) throw MaterialErrors.Invalid("Describe the condition of reusable materials.");
            if (doc.Kind is MaterialDocumentKind.ReturnUnused or MaterialDocumentKind.ReceiveRecovered)
            {
                if ((line.OriginalIssueLineId is null) == (line.RemovedMaterialId is null)) throw MaterialErrors.Invalid("Specify exactly one original issue line or recovered equipment part.");
                if (doc.Kind == MaterialDocumentKind.ReturnUnused && line.RemovedMaterialId is not null) throw MaterialErrors.Invalid("Unused returns require an issue line.");
                if (line.OriginalIssueLineId is { } issueLineId)
                {
                    var original = await store.IssueOfAsync(issueLineId, ct) ?? throw MaterialErrors.Missing("Issue line not found.");
                    if (original.Kind != MaterialDocumentKind.Issue || original.PostedAt is null || original.DepartmentId != actor.DepartmentId) throw MaterialErrors.Invalid("Select a posted issue from QTTB.");
                    if (originalId is { } previous && previous != original.Id) throw MaterialErrors.Invalid("A return must reference one issue document."); originalId = original.Id;
                    var sourceLine = original.Lines.Single(l => l.Id == issueLineId);
                    if (sourceLine.MaterialId != item.Id) throw MaterialErrors.Invalid("Returned material differs from the issue.");
                    doc.ServiceOrderId = original.ServiceOrderId;
                    if (doc.Kind == MaterialDocumentKind.ReceiveRecovered && item.Kind != MaterialKind.Tool) throw MaterialErrors.Invalid("Used materials need an equipment removal record; only used tools may return directly.");
                }
                else
                {
                    var source = await store.ChangeOfRemovalAsync(line.RemovedMaterialId!.Value, ct) ?? throw MaterialErrors.Missing("Equipment removal not found.");
                    var part = source.Removed.Single(r => r.Id == line.RemovedMaterialId);
                    doc.ServiceOrderId = source.ServiceOrderId;
                    if (!part.Reusable || part.Disposition != RemovedDisposition.ReturnToStock || part.MaterialId != item.Id || await changes.ExistsAsync(c => c.ReplacesId == source.Id, ct)) throw MaterialErrors.Invalid("The removed part is not an active reusable stock return.");
                }
                if (doc.Kind == MaterialDocumentKind.ReceiveRecovered && line.Condition != MaterialCondition.Reusable) throw MaterialErrors.Invalid("Recovered materials must be marked reusable.");
            }
            else if (line.OriginalIssueLineId is not null || line.RemovedMaterialId is not null) throw MaterialErrors.Invalid("Only return documents may reference issued or removed materials.");
            doc.Lines.Add(new MaterialDocumentLine { DocumentId = doc.Id, LineNo = ++index, MaterialId = item.Id, LocationId = location.Id, ToLocationId = toId,
                Quantity = line.Quantity, Condition = line.Condition, ConditionNote = Clean(line.ConditionNote), DeviceId = device?.Id,
                OriginalIssueLineId = line.OriginalIssueLineId, RemovedMaterialId = line.RemovedMaterialId, SourceLineCode = Clean(line.SourceLineCode), LotOrSerial = Clean(line.LotOrSerial),
                MaterialCodeSnapshot = item.Code, MaterialNameSnapshot = item.Name, UnitCodeSnapshot = unit.Code, UnitNameSnapshot = unit.Name });
        }
        if (doc.Lines.Where(l => l.OriginalIssueLineId != null).GroupBy(l => l.OriginalIssueLineId).Any(g => g.Count() > 1)
            || doc.Lines.Where(l => l.RemovedMaterialId != null).GroupBy(l => l.RemovedMaterialId).Any(g => g.Count() > 1)) throw MaterialErrors.Invalid("Duplicate return source lines.");
        await ValidateSourceAsync(doc, actor, ct);
    }
    private async Task DocumentAuditAsync(MaterialDocument doc, MaterialActor actor, string action, Guid? requestId, string? fingerprint, string? before, CancellationToken ct, string? reason = null)
        => await documentHistory.AddAsync(new MaterialDocumentHistory { DocumentId = doc.Id, RequestId = requestId, Fingerprint = fingerprint,
            ActorAccountId = actor.Id, Action = action, Reason = Clean(reason) ?? doc.Reason, BeforeData = before,
            AfterData = ProcurementRequests.ProcurementJson.Write(new { doc.Code, doc.Kind, doc.Status, doc.Version, doc.Reason, doc.Reference, doc.Notes, doc.ServiceOrderId, doc.ProcurementRequestId, doc.TargetDepartmentId, doc.PostedAt, doc.ReceivedAt, doc.Lines }), OccurredAt = clock.GetUtcNow() }, ct);
    public Task UpdateDocumentAsync(string code, UpdateMaterialDocument input, CancellationToken ct) => transaction.ExecuteAsync(async token =>
    {
        var actor = await access.RequireAsync(token); Validate(input); RequestId(input.RequestId);
        var doc = await ReadDocumentAsync(code, actor, true, token); var hash = Hash(new { code = doc.Code, action = "EDIT", input });
        if (await ReplayAsync(doc, actor, input.RequestId, hash, token)) return true;
        if (doc.PostedAt is not null || doc.Status is not (MaterialDocumentStatus.Ready or MaterialDocumentStatus.PendingApproval) || doc.Kind != input.Kind) throw MaterialErrors.Invalid("Only unposted, unapproved documents can be edited; their kind is immutable.");
        Version(doc.Version, input.Version); RequireRecorder(actor);
        var before = ProcurementRequests.ProcurementJson.Write(new { doc.Reason, doc.Version, doc.Reference, doc.Lines });
        await documentLines.DeleteRangeAsync(doc.Lines, token); doc.Lines.Clear();
        await FillDocumentAsync(doc, input, actor, token); doc.Version++;
        await DocumentAuditAsync(doc, actor, "EDIT", input.RequestId, hash, before, token); return true;
    }, ct);
    private async Task<bool> ReplayAsync(MaterialDocument doc, MaterialActor actor, Guid requestId, string hash, CancellationToken ct)
    {
        var prior = await documentHistory.FirstOrDefaultAsync(h => h.RequestId == requestId, ct);
        if (prior is null) return false;
        if (prior.DocumentId != doc.Id || prior.ActorAccountId != actor.Id || prior.Fingerprint != hash) throw MaterialErrors.Conflict("IDEMPOTENCY_CONFLICT", "requestId already belongs to another operation.");
        return true;
    }
    public Task ActAsync(string code, string action, MaterialAction input, CancellationToken ct) => transaction.ExecuteAsync(async token =>
    {
        var actor = await access.RequireAsync(token, allowDepartment: action == "receive"); Validate(input); RequestId(input.RequestId);
        var doc = await ReadDocumentAsync(code, actor, true, token); var hash = Hash(new { code = doc.Code, action, input });
        if (await ReplayAsync(doc, actor, input.RequestId, hash, token)) return true;
        Version(doc.Version, input.Version); var before = ProcurementRequests.ProcurementJson.Write(new { doc.Status, doc.Version });
        if (action is "approve" or "reject")
        {
            if (!actor.Manager) throw MaterialErrors.Forbidden();
            if (doc.Kind != MaterialDocumentKind.Adjustment || doc.Status != MaterialDocumentStatus.PendingApproval) throw MaterialErrors.Invalid("Only pending stock adjustments require approval.");
            if (action == "reject" && Clean(input.Reason) is null) throw MaterialErrors.Invalid("A rejection reason is required.");
            doc.Status = action == "approve" ? MaterialDocumentStatus.Approved : MaterialDocumentStatus.Rejected;
            doc.ApprovedByAccountId = actor.Id; doc.ApprovedAt = clock.GetUtcNow();
        }
        else if (action == "post")
        {
            RequireRecorder(actor);
            if (doc.Status != (doc.Kind == MaterialDocumentKind.Adjustment ? MaterialDocumentStatus.Approved : MaterialDocumentStatus.Ready)) throw MaterialErrors.Invalid("The document is not ready to post.");
            await ValidateSourceAsync(doc, actor, token);
            await PostAsync(doc, actor, token);
            doc.PostedByAccountId = actor.Id; doc.PostedAt = clock.GetUtcNow();
            doc.Status = doc.Kind == MaterialDocumentKind.Issue ? MaterialDocumentStatus.AwaitingReceipt : MaterialDocumentStatus.Completed;
        }
        else if (action == "receive")
        {
            if (doc.Kind != MaterialDocumentKind.Issue || doc.Status != MaterialDocumentStatus.AwaitingReceipt) throw MaterialErrors.Invalid("The issue is not awaiting receipt.");
            if (actor.Department) { if (doc.TargetDepartmentId != actor.DepartmentId) throw MaterialErrors.Forbidden(); }
            else
            {
                RequireRecorder(actor);
                if (doc.TargetDepartmentId != doc.DepartmentId) throw MaterialErrors.Forbidden("The receiving department must confirm delivery.");
            }
            // Mixed equipment/material delivery must be coordinated before acknowledging either branch.
            await CompleteCombinedDeliveryAsync(doc, input, actor, token);
            doc.ReceivedByAccountId = actor.Id; doc.ReceivedAt = clock.GetUtcNow(); doc.Status = MaterialDocumentStatus.Completed;
        }
        else if (action == "cancel")
        {
            if (!actor.Manager) RequireRecorder(actor);
            if (Clean(input.Reason) is null) throw MaterialErrors.Invalid("A cancellation reason is required.");
            if (doc.PostedAt is not null || doc.Status is MaterialDocumentStatus.Cancelled or MaterialDocumentStatus.Rejected) throw MaterialErrors.Invalid("Posted or closed documents cannot be cancelled. Record a return or an approved adjustment.");
            doc.Status = MaterialDocumentStatus.Cancelled;
        }
        else throw MaterialErrors.Invalid("Unknown material action.");
        doc.Version++; await DocumentAuditAsync(doc, actor, action.ToUpperInvariant(), input.RequestId, hash, before, token, input.Reason); return true;
    }, ct);
    private async Task PostAsync(MaterialDocument doc, MaterialActor actor, CancellationToken ct)
    {
        var cache = new Dictionary<(Guid, Guid), MaterialBalance>();
        async Task Move(MaterialDocumentLine line, Guid locationId, decimal delta, int leg)
        {
            await ActiveLocationAsync(locationId, actor.DepartmentId, ct);
            var key = (line.MaterialId, locationId);
            if (!cache.TryGetValue(key, out var balance))
            {
                balance = await store.BalanceAsync(line.MaterialId, locationId, ct);
                if (balance is null) { balance = new MaterialBalance { MaterialId = line.MaterialId, LocationId = locationId }; await balances.AddAsync(balance, ct); }
                cache.Add(key, balance);
            }
            if (balance.Quantity + delta < 0) throw MaterialErrors.Conflict("MATERIAL_INSUFFICIENT_STOCK", "Insufficient stock; posting would make the balance negative.");
            balance.Quantity += delta; balance.Version++;
            await ledger.AddAsync(new MaterialLedgerEntry { DocumentId = doc.Id, LineId = line.Id, Leg = leg, MaterialId = line.MaterialId,
                LocationId = locationId, Delta = delta, BalanceAfter = balance.Quantity, ActorAccountId = actor.Id, OccurredAt = clock.GetUtcNow() }, ct);
        }
        foreach (var line in doc.Lines.OrderBy(l => l.LineNo))
        {
            var item = await materials.GetByIdAsync(line.MaterialId, ct) ?? throw MaterialErrors.Missing();
            var unit = await units.GetByIdAsync(item.UnitId, ct) ?? throw MaterialErrors.Missing();
            if (item.Status != EntityStatus.Active || unit.Status != EntityStatus.Active) throw MaterialErrors.Invalid("The material or unit is locked.");
            Quantity(line.Quantity, unit.DecimalPlaces, doc.Kind == MaterialDocumentKind.Adjustment);
            if (line.OriginalIssueLineId is { } issueLineId)
            {
                var original = await store.IssueOfAsync(issueLineId, ct) ?? throw MaterialErrors.Missing();
                if (original.Kind != MaterialDocumentKind.Issue || original.PostedAt is null || original.Status == MaterialDocumentStatus.Cancelled) throw MaterialErrors.Invalid("The original issue is unavailable.");
                var issued = original.Lines.Single(l => l.Id == issueLineId);
                var used = await store.UsedAsync(issueLineId, null, ct);
                var remaining = doc.Kind == MaterialDocumentKind.ReturnUnused
                    ? issued.Quantity - used - await store.ReturnedAsync(issueLineId, MaterialDocumentKind.ReturnUnused, ct)
                    : used - await store.ReturnedAsync(issueLineId, MaterialDocumentKind.ReceiveRecovered, ct);
                if (line.Quantity > remaining) throw MaterialErrors.Conflict("MATERIAL_RETURN_EXCEEDS_ISSUE", "Returned quantity exceeds the outstanding quantity.");
            }
            if (line.RemovedMaterialId is { } removedId)
            {
                var source = await store.ChangeOfRemovalAsync(removedId, ct) ?? throw MaterialErrors.Missing();
                var part = source.Removed.Single(r => r.Id == removedId);
                if (await changes.ExistsAsync(c => c.ReplacesId == source.Id, ct) || !part.Reusable || part.Disposition != RemovedDisposition.ReturnToStock || part.MaterialId != line.MaterialId || line.Quantity + await store.RecoveredAsync(removedId, ct) > part.Quantity)
                    throw MaterialErrors.Conflict("MATERIAL_RECOVERY_EXCEEDED", "The removed part is unavailable or its recoverable quantity is exceeded.");
            }
            await Move(line, line.LocationId, doc.Kind is MaterialDocumentKind.Issue or MaterialDocumentKind.Transfer ? -line.Quantity : line.Quantity, 0);
            if (doc.Kind == MaterialDocumentKind.Transfer) await Move(line, line.ToLocationId!.Value, line.Quantity, 1);
        }
    }
}
