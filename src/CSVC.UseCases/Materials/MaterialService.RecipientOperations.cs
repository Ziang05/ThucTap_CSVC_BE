using CSVC.Core.Materials;
using CSVC.UseCases.Common;

namespace CSVC.UseCases.Materials;

public sealed partial class MaterialService
{
    private static void RequireRecipient(MaterialDocument issue, MaterialActor actor)
    {
        if (issue.Kind != MaterialDocumentKind.Issue || issue.PostedAt is null || issue.Status != MaterialDocumentStatus.Completed)
            throw MaterialErrors.Invalid("Select a completed material issue.");
        if (issue.TargetDepartmentId != actor.DepartmentId || !(actor.Department || actor.Staff))
            throw MaterialErrors.Forbidden("Only the receiving department may record consumption or return materials.");
    }

    private async Task<decimal> PendingReturnAsync(Guid issueLineId, MaterialDocumentKind kind, Guid? excludingDocumentId, CancellationToken ct)
    {
        var lines = await documentLines.FindAsync(l => l.OriginalIssueLineId == issueLineId && l.DocumentId != excludingDocumentId, ct);
        var ids = lines.Select(l => l.DocumentId).ToArray();
        var pending = await documents.FindAsync(d => ids.Contains(d.Id) && d.Kind == kind && d.PostedAt == null
            && (d.Status == MaterialDocumentStatus.PendingApproval || d.Status == MaterialDocumentStatus.Approved), ct);
        var pendingIds = pending.Select(d => d.Id).ToHashSet();
        return lines.Where(l => pendingIds.Contains(l.DocumentId)).Sum(l => l.Quantity);
    }

    private async Task<MaterialDocument> CreateRecipientReturnAsync(CreateMaterialDocument input, MaterialActor actor, string hash, CancellationToken ct)
    {
        if (input.Lines is null || input.Lines.Count is < 1 or > 200 || input.Lines.Any(l => l is null || l.OriginalIssueLineId == null || l.RemovedMaterialId != null))
            throw MaterialErrors.Invalid("Return documents must reference original issue lines.");
        if (input.Lines.Select(l => l.OriginalIssueLineId).Distinct().Count() != input.Lines.Count)
            throw MaterialErrors.Invalid("Duplicate return source lines.");
        MaterialDocument? original = null;
        var doc = new MaterialDocument { Code = FeatureCode.New("PV"), Kind = input.Kind, Status = MaterialDocumentStatus.PendingApproval,
            CreateRequestId = input.RequestId, CreateHash = hash, CreatedByAccountId = actor.Id, CreatedAt = clock.GetUtcNow(),
            Reason = Name(input.Reason), Reference = Clean(input.Reference), ReferenceDate = input.ReferenceDate, Notes = Clean(input.Notes) };
        foreach (var inputLine in input.Lines)
        {
            Validate(inputLine);
            var issue = await store.IssueOfAsync(inputLine.OriginalIssueLineId!.Value, ct) ?? throw MaterialErrors.Missing();
            RequireRecipient(issue, actor);
            if (original is not null && original.Id != issue.Id) throw MaterialErrors.Invalid("A return must reference one issue document.");
            original = issue;
            var line = issue.Lines.Single(l => l.Id == inputLine.OriginalIssueLineId);
            if (Code(inputLine.MaterialCode) != line.MaterialCodeSnapshot) throw MaterialErrors.Invalid("Returned material differs from the issue.");
            var material = (await materials.GetByIdAsync(line.MaterialId, ct))!;
            var unit = (await units.GetByIdAsync(material.UnitId, ct))!;
            Quantity(inputLine.Quantity, unit.DecimalPlaces);
            var used = await store.UsedAsync(line.Id, null, ct);
            if (input.Kind == MaterialDocumentKind.ReceiveRecovered && material.Kind != MaterialKind.Tool)
                throw MaterialErrors.Invalid("Only used tools may be returned directly.");
            var remaining = input.Kind == MaterialDocumentKind.ReturnUnused
                ? line.Quantity - used - await store.ReturnedAsync(line.Id, input.Kind, ct)
                : used - await store.ReturnedAsync(line.Id, input.Kind, ct);
            if (inputLine.Quantity + await PendingReturnAsync(line.Id, input.Kind, null, ct) > remaining)
                throw MaterialErrors.Conflict("MATERIAL_RETURN_EXCEEDS_ISSUE", "Returned quantity exceeds the outstanding quantity, including pending returns.");
            if (input.Kind == MaterialDocumentKind.ReceiveRecovered && Clean(inputLine.ConditionNote) is null)
                throw MaterialErrors.Invalid("Describe the returned tool's condition.");
            doc.Lines.Add(new MaterialDocumentLine { DocumentId = doc.Id, LineNo = doc.Lines.Count + 1,
                MaterialId = line.MaterialId, LocationId = line.LocationId, DeviceId = line.DeviceId, OriginalIssueLineId = line.Id,
                Quantity = inputLine.Quantity, Condition = input.Kind == MaterialDocumentKind.ReturnUnused ? line.Condition : MaterialCondition.Reusable,
                ConditionNote = Clean(inputLine.ConditionNote) ?? line.ConditionNote, LotOrSerial = line.LotOrSerial,
                MaterialCodeSnapshot = line.MaterialCodeSnapshot, MaterialNameSnapshot = line.MaterialNameSnapshot,
                UnitCodeSnapshot = line.UnitCodeSnapshot, UnitNameSnapshot = line.UnitNameSnapshot });
        }
        doc.DepartmentId = original!.DepartmentId; doc.TargetDepartmentId = original.TargetDepartmentId;
        doc.TargetResponsibleAccountId = original.TargetResponsibleAccountId; doc.ServiceOrderId = original.ServiceOrderId;
        await documents.AddAsync(doc, ct);
        await DocumentAuditAsync(doc, actor, "RETURN_SUBMITTED", null, null, null, ct);
        return doc;
    }

    private async Task<MaterialDocument> CreateInterventionIssueAsync(InterventionIssueInput input, string deviceCode,
        string? serviceOrderCode, Guid requestId, MaterialActor actor, CancellationToken ct)
    {
        Validate(input);
        if (input.Lines is null || input.Lines.Count is < 1 or > 100) throw MaterialErrors.Invalid("Supply 1 to 100 material issue lines.");
        var lines = new List<MaterialLineInput>();
        foreach (var line in input.Lines)
        {
            if (line is null) throw MaterialErrors.Invalid("Issue lines cannot be null.");
            Validate(line);
            if (line.OriginalIssueLineId is not null || line.RemovedMaterialId is not null || Clean(line.ToLocationCode) is not null
                || (Clean(line.DeviceCode) is not null && Code(line.DeviceCode) != deviceCode))
                throw MaterialErrors.Invalid("Issue materials for the current equipment only.");
            lines.Add(new MaterialLineInput { MaterialCode = line.MaterialCode, Quantity = line.Quantity,
                Condition = line.Condition, ConditionNote = line.ConditionNote, DeviceCode = deviceCode, LotOrSerial = line.LotOrSerial });
        }
        var request = new CreateMaterialDocument { RequestId = requestId, Kind = MaterialDocumentKind.Issue,
            Reason = input.Reason, Reference = input.Reference, ReferenceDate = input.ReferenceDate, Notes = input.Notes,
            TargetDepartmentCode = input.TargetDepartmentCode, TargetResponsibleAccountCode = input.TargetResponsibleAccountCode,
            ServiceOrderCode = serviceOrderCode, Lines = lines };
        var doc = new MaterialDocument { Code = FeatureCode.New("PV"), Kind = MaterialDocumentKind.Issue,
            Status = MaterialDocumentStatus.Ready, DepartmentId = actor.DepartmentId, CreateRequestId = requestId,
            CreateHash = Hash(request), CreatedByAccountId = actor.Id, CreatedAt = clock.GetUtcNow(), Reason = Name(input.Reason) };
        await FillDocumentAsync(doc, request, actor, ct);
        await documents.AddAsync(doc, ct);
        await DocumentAuditAsync(doc, actor, "CREATED", null, null, null, ct);
        // Persist unposted lines before the posting guard makes them immutable, within the same transaction.
        await uow.CommitAsync(ct);
        await PostAsync(doc, actor, ct);
        doc.PostedByAccountId = actor.Id; doc.PostedAt = clock.GetUtcNow(); doc.Status = MaterialDocumentStatus.Completed; doc.Version++;
        await DocumentAuditAsync(doc, actor, "POST", null, null, null, ct);
        return doc;
    }
}
