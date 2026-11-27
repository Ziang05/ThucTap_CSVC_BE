using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using CSVC.Core.Accounts;
using CSVC.Core.Common;
using CSVC.Core.Devices;
using CSVC.Core.ProcurementRequests;
using CSVC.Core.ServiceOrders;
using CSVC.UseCases.Abstractions.Persistence;
using CSVC.UseCases.Abstractions.Security;
using CSVC.UseCases.Common;
using CSVC.UseCases.Exceptions;
using CSVC.UseCases.Settings;

namespace CSVC.UseCases.ProcurementRequests;

public sealed partial class ProcurementRequestService(IProcurementRequestRepository requests, IRepository<ProcurementRequest> records,
    IRepository<ProcurementRequestLine> lines, IRepository<ProcurementRequestEvent> events, IPmmsRequestSource source,
    IProcurementWorkflow workflow, IUnitOfWork uow, IInventoryTransaction transaction, ICurrentAccount current,
    IEquipmentDepartmentPolicy equipment, IServiceOrderRepository services, IRepository<ServiceOrderDeviceHold> serviceHolds,
    IRepository<CSVC.Core.DamageReports.DamageReport> damageReports, TimeProvider clock,
    CSVC.UseCases.Materials.IMaterialWorkflow materialWorkflow, IRequestAssignmentPolicy assignment,
    CSVC.UseCases.ServiceOrders.IServiceOrderService repairExecution,
    CSVC.UseCases.DeviceTransfers.IDeviceTransferService purchaseDelivery) : IProcurementRequestService
{
    public Task<PmmsEventResult> SimulateAsync(PmmsSimulatedEvent input, CancellationToken ct)
        => transaction.ExecuteAsync(async token =>
        {
            var sourceActor = await ReadActorAsync(token);
            if (sourceActor.Role != RoleCodes.EquipmentManager) throw ForbiddenSource();
            var actor = sourceActor.Id;
            var normalized = source.Normalize(input);
            var payload = ProcurementJson.Write(new { normalized.ExternalCode, normalized.SourceVersion, normalized.Action, normalized.Request, normalized.Reason });
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
            var prior = await events.FirstOrDefaultAsync(e => e.ExternalEventId == normalized.EventId, token);
            if (prior is not null) return Replay(prior, hash);
            var request = await requests.GetBySourceAsync(source.Source, normalized.ExternalCode, token);
            if (request is not null)
            {
                prior = await events.FirstOrDefaultAsync(e => e.RequestId == request.Id && e.SourceVersion == normalized.SourceVersion, token);
                if (prior is not null) return Replay(prior, hash);
            }
            else
            {
                if (normalized.Action != PmmsEventAction.Create) throw Missing();
                request = new ProcurementRequest { DisplayCode = CSVC.UseCases.Common.FeatureCode.New("YC"), ExternalCode = normalized.ExternalCode,
                    WorkflowVersion = 2, Source = source.Source, Title = normalized.Request!.Title, RequestingDepartmentCode = normalized.Request.RequestingDepartmentCode,
                    SourceStatus = normalized.Request.SourceStatus, ReceivedAt = clock.GetUtcNow() };
                await records.AddAsync(request, token);
            }
            string outcome; string? reason = null;
            if (normalized.SourceVersion <= request.SourceVersion) outcome = "IgnoredStale";
            else if (request.Status is ProcurementRequestStatus.Completed or ProcurementRequestStatus.Cancelled)
            {
                outcome = "IgnoredTerminal"; reason = "The local request is already closed.";
            }
            else if (normalized.Action == PmmsEventAction.Cancel)
            {
                var documents = await workflow.DocumentsAsync(request, token);
                if (documents.Any(d => d.Executed || d.Open))
                {
                    outcome = "CancelRejected"; reason = documents.Any(d => d.Executed)
                        ? "Work has already been performed; QTTB must finish the business process."
                        : "Cancel all pending documents and release their holds before cancelling the source request.";
                }
                else
                {
                    outcome = "Cancelled"; reason = normalized.Reason;
                    request.Status = ProcurementRequestStatus.Cancelled; request.ResultReason = normalized.Reason; request.ClosedAt = clock.GetUtcNow();
                }
            }
            else
            {
                var documents = await workflow.DocumentsAsync(request, token);
                if (documents.Any(d => d.Open || d.Executed))
                {
                    outcome = "NeedsReview"; reason = "Documents are already linked. Source changes must not overwrite their scope.";
                    request.RequiresReview = true; request.PendingSourcePayload = ProcurementJson.Write(normalized.Request);
                }
                else
                {
                    await ApplyAsync(request, normalized.Request!, token);
                    outcome = request.MappingIssue is null ? "Applied" : "NeedsMapping";
                    reason = request.MappingIssue;
                }
            }
            if (normalized.SourceVersion > request.SourceVersion) request.SourceVersion = normalized.SourceVersion;
            request.Version++;
            var result = new PmmsEventResult(request.DisplayCode, outcome, reason, request.SourceVersion, request.Version);
            await events.AddAsync(new ProcurementRequestEvent { RequestId = request.Id, ExternalEventId = normalized.EventId,
                SourceVersion = normalized.SourceVersion, Action = normalized.Action.ToString(), Outcome = outcome, Reason = reason,
                PayloadHash = hash, Payload = payload, Response = ProcurementJson.Write(result), ActorAccountId = actor, OccurredAt = clock.GetUtcNow() }, token);
            return result;
        }, ct);

    private static PmmsEventResult Replay(ProcurementRequestEvent prior, string hash)
    {
        if (prior.PayloadHash != hash) throw Conflict("PMMS_EVENT_CONFLICT", "The same source event or revision was sent with different content.");
        return ProcurementJson.Read<PmmsEventResult>(prior.Response!);
    }

    private async Task ApplyAsync(ProcurementRequest request, PmmsRequestPayload payload, CancellationToken ct)
    {
        var issues = new List<string>();
        if (!await uow.Departments.ExistsAsync(d => d.Code == payload.RequestingDepartmentCode && d.Status == EntityStatus.Active, ct)) issues.Add("Requesting department is unknown or inactive.");
        foreach (var line in payload.Lines)
        {
            if (line.CategoryCode is { } category && !await uow.DeviceCategories.ExistsAsync(c => c.DisplayCode == category && c.Status == EntityStatus.Active, ct)) issues.Add($"Unknown or inactive category on line {line.SourceLineCode}.");
            if (payload.RequestType != ProcurementRequestType.Repair) continue;
            var device = line.DeviceCode is null ? null : await uow.Devices.FirstOrDefaultAsync(d => d.DeviceCode == line.DeviceCode, ct);
            if (device is null) issues.Add($"Repair device cannot be resolved on line {line.SourceLineCode}.");
            if (line.DamageReportCode is { } report && (device is null || !await damageReports.ExistsAsync(r => r.ReportCode == report && r.DeviceId == device.Id, ct))) issues.Add($"Damage report cannot be resolved on line {line.SourceLineCode}.");
        }
        // Only unexecuted, unbound scopes may be replaced; events retain every source snapshot.
        var incomingCodes = payload.Lines.Select(l => l.SourceLineCode).ToHashSet();
        var removed = request.Lines.Where(l => !incomingCodes.Contains(l.SourceLineCode)).ToArray();
        await lines.DeleteRangeAsync(removed, ct);
        foreach (var old in removed) request.Lines.Remove(old);
        foreach (var input in payload.Lines)
        {
            var line = request.Lines.SingleOrDefault(l => l.SourceLineCode == input.SourceLineCode);
            if (line is null)
            {
                line = new ProcurementRequestLine { RequestId = request.Id, SourceLineCode = input.SourceLineCode, Description = input.Description, Unit = input.Unit };
                request.Lines.Add(line);
            }
            line.Kind = input.Kind; line.Description = input.Description; line.Quantity = input.Quantity;
            line.Unit = input.Unit; line.CategoryCode = input.CategoryCode; line.DeviceCode = input.DeviceCode;
            line.DamageReportCode = input.DamageReportCode; line.Specialization = input.Specialization;
        }
        request.RequestType = payload.RequestType; request.Title = payload.Title; request.RequestingDepartmentCode = payload.RequestingDepartmentCode;
        request.Contact = payload.Contact; request.ContractReference = payload.ContractReference; request.SupplierReference = payload.SupplierReference;
        request.Notes = payload.Notes; request.SourceStatus = payload.SourceStatus; request.SourceCreatedAt = payload.CreatedAt; request.SourceUpdatedAt = payload.UpdatedAt;
        var mappingIssue = string.Join(" ", issues.Distinct());
        request.MappingIssue = issues.Count == 0 ? null : mappingIssue[..Math.Min(mappingIssue.Length, 1000)];
        request.RequiresReview = request.MappingIssue is not null; request.PendingSourcePayload = null;
    }

    public async Task<PagedResult<ProcurementSummary>> ListAsync(ProcurementQuery query, CancellationToken ct)
    {
        var actor = await ReadActorAsync(ct); Validate(query);
        if (query.AssignedToMe && query.Unassigned) throw Invalid("Choose one responsibility filter.");
        return await requests.ListAsync(query, ct, actor.Role == RoleCodes.DepartmentStaff ? actor.DepartmentCode : null,
            actor.Role == RoleCodes.EquipmentStaff || query.AssignedToMe ? actor.Id : null);
    }
    public async Task<ProcurementDetail> GetAsync(string code, CancellationToken ct)
    {
        var actor = await ReadActorAsync(ct);
        var request = await requests.GetAsync(code, false, ct) ?? throw Missing();
        RequireVisible(request, actor);
        var documents = await workflow.DocumentsAsync(request, ct);
        var repairResults = new List<RepairResultDto>();
        foreach (var document in documents.Where(d => d.Kind == RequestDocumentKind.Repair))
        {
            var order = (await services.GetAggregateAsync(document.DocumentCode, false, ct))!;
            repairResults.AddRange(order.Items.Select(i => new RepairResultDto(i.DeviceCodeSnapshot, i.ExecutionStatus?.ToString(), i.ResultDescription, i.ResultRecordedAt)));
        }
        return new(Summary(request), request.Source == "PMMS_SIMULATED", request.Contact, request.ContractReference, request.SupplierReference,
            request.SourceStatus, request.SourceCreatedAt, request.SourceUpdatedAt, request.Notes, request.MappingIssue, request.PendingSourcePayload,
            request.Result, request.ResultReason, request.ClosedAt,
            request.Lines.OrderBy(l => l.SourceLineCode).Select(l => new ProcurementLineDto(l.SourceLineCode, l.Kind, l.Description, l.Quantity, l.Unit, l.CategoryCode, l.DeviceCode, l.DamageReportCode, l.Specialization)).ToArray(),
            documents, request.Status is not (ProcurementRequestStatus.Completed or ProcurementRequestStatus.Cancelled) && !documents.Any(d => d.Executed || d.Open))
        {
            Assignment = await AssignmentAsync(request, ct),
            CanAssign = actor.Role == RoleCodes.EquipmentManager && request.Status is not (ProcurementRequestStatus.Completed or ProcurementRequestStatus.Cancelled),
            CanExecute = actor.Role == RoleCodes.EquipmentStaff && request.AssigneeAccountId == actor.Id && request.Status is not (ProcurementRequestStatus.Completed or ProcurementRequestStatus.Cancelled),
            RequiresResultAcceptance = NeedsAcceptance(request),
            CanAcceptResults = IsReceiver(request, actor),
            CanConfirmReceipt = IsReceiver(request, actor) && request.RequestType == ProcurementRequestType.Purchase
                && request.Status == ProcurementRequestStatus.AwaitingReceipt
                && !request.RequiresReview && request.MappingIssue is null
                && !request.Lines.Any(line => line.Kind == ProcurementLineKind.Material),
            RepairResults = repairResults,
            ServiceEvidence = request.ServiceEvidenceJson is null ? null : new(ProcurementJson.Read<ServiceEvidenceLine[]>(request.ServiceEvidenceJson),
                (await uow.Accounts.GetByIdAsync(request.ServiceRecordedByAccountId!.Value, ct))!.AccountCode, request.ServiceRecordedAt!.Value),
            Acceptance = request.AcceptanceDecision is null ? null : new(request.AcceptanceDecision, request.AcceptanceReason,
                (await uow.Accounts.GetByIdAsync(request.AcceptedByAccountId!.Value, ct))!.AccountCode, request.AcceptedAt!.Value)
        };
    }
    public async Task<PagedResult<ProcurementEventDto>> HistoryAsync(string code, int page, int pageSize, CancellationToken ct)
    {
        var actor = await ReadActorAsync(ct);
        if (page < 1 || page > 1000000 || pageSize < 1 || pageSize > 100) throw Invalid("Invalid paging.");
        var request = await requests.GetAsync(code, false, ct) ?? throw Missing();
        RequireVisible(request, actor);
        return await requests.HistoryAsync(request.Id, page, pageSize, ct);
    }
    public Task TransitionAsync(string code, ProcurementTransition input, CancellationToken ct)
        => transaction.ExecuteAsync(async token =>
        {
            var actor = await ActorAsync(true, token); Validate(input);
            var request = await EditableAsync(code, input.Version, token);
            await assignment.RequireAsync(code, actor, token);
            if (request.RequiresReview) throw Conflict("SOURCE_REQUEST_NOT_READY", "Resolve mapping or source changes first.");
            if (request.RequestType == ProcurementRequestType.Purchase && request.Lines.Any(line => line.Kind == ProcurementLineKind.Equipment))
                throw Conflict("PURCHASE_STATUS_AUTOMATIC", "Purchase progress follows receipts, allocation and physical delivery confirmation.");
            if (input.Status is not (ProcurementRequestStatus.InProgress or ProcurementRequestStatus.WaitingForGoods or ProcurementRequestStatus.WaitingForExternal or ProcurementRequestStatus.AwaitingHandover)) throw Invalid("Use the completion or source cancellation operation to close a request.");
            request.Status = input.Status; request.Version++;
            await AuditAsync(request, actor, "STATUS_CHANGED", input.Status.ToString(), input.Reason, token);
            return true;
        }, ct);
    public Task ReviewAsync(string code, ProcurementTransition input, CancellationToken ct)
        => transaction.ExecuteAsync(async token =>
        {
            var actor = await ActorAsync(true, token); Validate(input);
            var account = await uow.Accounts.GetByIdAsync(actor, token);
            if (account?.RoleId != 1) throw new UseCaseException(FailureKind.Forbidden, "SOURCE_REVIEW_FORBIDDEN", "Only the QTTB manager may retain the existing scope after review.");
            var request = await EditableAsync(code, input.Version, token);
            if (!request.RequiresReview || request.PendingSourcePayload is null || request.MappingIssue is not null || string.IsNullOrWhiteSpace(input.Reason)) throw Invalid("Review requires a pending source update and a reason for keeping the current scope. Correct unresolved references through a new source event.");
            request.RequiresReview = false; request.PendingSourcePayload = null; request.Version++;
            await AuditAsync(request, actor, "SOURCE_SCOPE_REVIEWED", "ExistingScopeRetained", input.Reason, token);
            await workflow.RefreshPurchaseAsync(request.Id, actor, token);
            return true;
        }, ct);
    public Task CompleteAsync(string code, ProcurementClose input, CancellationToken ct)
        => transaction.ExecuteAsync(async token =>
        {
            var actor = await ActorAsync(true, token); Validate(input);
            var completed = await requests.GetAsync(code, false, token) ?? throw Missing();
            if (completed.Status == ProcurementRequestStatus.Completed && completed.RequestType == ProcurementRequestType.Purchase
                && completed.Result == "Success" && completed.Lines.Any(line => line.Kind == ProcurementLineKind.Equipment)
                && !completed.Lines.Any(line => line.Kind == ProcurementLineKind.Service) && string.IsNullOrWhiteSpace(input.FailureReason))
            {
                await assignment.RequireIdAsync(completed.Id, actor, token, allowClosed: true);
                if (completed.Version != input.Version) throw Conflict("VERSION_CONFLICT", "Reload the request before saving.");
                return true;
            }
            var request = await EditableAsync(code, input.Version, token);
            var closingActor = await ReadActorAsync(token);
            if (!(closingActor.Role == RoleCodes.EquipmentManager && request.AcceptanceDecision == "Rejected" && !string.IsNullOrWhiteSpace(input.FailureReason)))
                await assignment.RequireAsync(code, actor, token);
            if (request.RequiresReview) throw Conflict("SOURCE_REQUEST_NOT_READY", "Resolve mapping or source changes first.");
            var result = await ValidateCompletionAsync(request, input.FailureReason, token);
            var documents = await workflow.DocumentsAsync(request, token);
            if (NeedsAcceptance(request) && (string.IsNullOrWhiteSpace(input.FailureReason) || documents.Any(d => d.Executed)))
            {
                var hash = await CompletionHashAsync(request, token);
                if (request.AcceptanceScopeHash != hash || request.AcceptanceDecision is null)
                    throw Conflict("RESULT_ACCEPTANCE_REQUIRED", "The requesting department must confirm the complete result first.");
                if (request.AcceptanceDecision != "Accepted")
                {
                    var account = (await uow.Accounts.GetByIdAsync(actor, token))!;
                    if (string.IsNullOrWhiteSpace(input.FailureReason) || (await uow.Roles.GetByIdAsync(account.RoleId, token))?.Code != RoleCodes.EquipmentManager)
                        throw Conflict("RESULT_REJECTED", "Only the manager may close a rejected result as Failed with a reason, or record corrected service evidence.");
                }
            }
            // All lines close together; partial completion is never stored.
            request.Status = ProcurementRequestStatus.Completed; request.Result = result; request.ResultReason = input.FailureReason;
            request.ClosedAt = clock.GetUtcNow(); request.Version++;
            await AuditAsync(request, actor, "REQUEST_COMPLETED", result, input.FailureReason, token);
            return true;
        }, ct);

    private async Task<ProcurementRequest> EditableAsync(string code, int version, CancellationToken ct)
    {
        var request = await requests.GetAsync(code, true, ct) ?? throw Missing();
        if (request.Version != version) throw Conflict("VERSION_CONFLICT", "Reload the source request before saving.");
        if (request.Status is ProcurementRequestStatus.Completed or ProcurementRequestStatus.Cancelled) throw Conflict("SOURCE_REQUEST_CLOSED", "The request is already closed.");
        return request;
    }
    private async Task<Guid> ActorAsync(bool writing, CancellationToken ct)
    {
        var actor = await ReadActorAsync(ct);
        if (actor.Role == RoleCodes.DepartmentStaff) throw ForbiddenSource();
        return actor.Id;
    }
    private Task AuditAsync(ProcurementRequest request, Guid actor, string action, string outcome, string? reason, CancellationToken ct)
        => events.AddAsync(new ProcurementRequestEvent { RequestId = request.Id, Action = action, Outcome = outcome,
            Reason = reason, ActorAccountId = actor, OccurredAt = clock.GetUtcNow() }, ct);
    private static ProcurementSummary Summary(ProcurementRequest r) => new(r.DisplayCode, r.Source, r.ExternalCode, r.RequestType, r.Title, r.RequestingDepartmentCode, r.Status, r.RequiresReview, r.SourceVersion, r.Version, r.ReceivedAt);
    private static void Validate(object value) { if (!Validator.TryValidateObject(value, new ValidationContext(value), [], true)) throw Invalid("Invalid request fields."); }
    private static UseCaseException Missing() => new(FailureKind.NotFound, "SOURCE_REQUEST_NOT_FOUND", "The source request was not found.");
    private static UseCaseException Invalid(string reason) => new(FailureKind.Validation, "VALIDATION_FAILED", reason);
    private static UseCaseException Unauthorized() => new(FailureKind.Unauthorized, "AUTHENTICATION_REQUIRED", "A valid session is required.");
    private static UseCaseException Conflict(string code, string reason) => new(FailureKind.Conflict, code, reason);

    private async Task<string> ValidateCompletionAsync(ProcurementRequest request, string? failureReason, CancellationToken ct)
    {
            var documents = await workflow.DocumentsAsync(request, ct);
            if (documents.Any(d => d.Open)) throw Conflict("SOURCE_DOCUMENTS_OPEN", "Finish or cancel all open documents before closing the request.");
            foreach (var repairDocument in documents.Where(d => d.Kind == RequestDocumentKind.Repair && d.Executed))
            {
                var repairOrder = (await services.GetAggregateAsync(repairDocument.DocumentCode, false, ct))!;
                if (await serviceHolds.ExistsAsync(h => h.ServiceOrderId == repairOrder.Id, ct)) throw Conflict("SOURCE_REQUEST_INCOMPLETE", "Receive exported devices and reconcile repair reservations before closing.");
            }
            string result;
            if (!string.IsNullOrWhiteSpace(failureReason)) result = "Failed";
            else if (request.RequestType == ProcurementRequestType.Repair)
            {
                var repair = documents.SingleOrDefault(d => d.Kind == RequestDocumentKind.Repair && d.Status == "Completed") ?? throw Conflict("SOURCE_REQUEST_INCOMPLETE", "Complete every repair line first.");
                var order = (await services.GetAggregateAsync(repair.DocumentCode, false, ct))!;
                if (await serviceHolds.ExistsAsync(h => h.ServiceOrderId == order.Id, ct)) throw Conflict("SOURCE_REQUEST_INCOMPLETE", "Receive exported devices and reconcile repair reservations first.");
                result = order.Items.All(i => i.ExecutionStatus == ServiceItemStatus.Completed) ? "Success" : "CompletedWithIssues";
            }
            else
            {
                RequireServiceEvidence(request);
                await materialWorkflow.RequireRequestReadyAsync(request, ct);
                var allocation = documents.SingleOrDefault(d => (d.Kind == RequestDocumentKind.Allocation || d.Kind == RequestDocumentKind.InternalUse) && d.Status == "Completed");
                if (allocation is null && request.Lines.Any(l => l.Kind == ProcurementLineKind.Equipment))
                {
                    var equipmentId = await equipment.RequireConfiguredAsync(ct);
                    if (!await uow.Departments.ExistsAsync(d => d.Id == equipmentId && d.Code == request.RequestingDepartmentCode, ct)) throw Conflict("SOURCE_REQUEST_INCOMPLETE", "The receiving department must confirm the separate allocation.");
                    var receipt = documents.SingleOrDefault(d => d.Kind == RequestDocumentKind.Receipt && d.Status == "Completed") ?? throw Conflict("SOURCE_REQUEST_INCOMPLETE", "Receive all devices before confirming internal use.");
                    foreach (var line in request.Lines.Where(l => l.Kind == ProcurementLineKind.Equipment))
                        if (receipt.Bindings.Count(b => b.SourceLineCode == line.SourceLineCode) != line.Quantity) throw Conflict("SOURCE_REQUEST_INCOMPLETE", "Every requested device must be received and put into use.");
                    var codes = receipt.Bindings.Select(b => b.DeviceCode).ToArray();
                    var devices = await uow.Devices.FindAsync(d => codes.Contains(d.DeviceCode), ct);
                    if (devices.Count != codes.Length || devices.Any(d => d.DepartmentId != equipmentId || d.UseState != DeviceUseState.InUse || d.Quantity != 1)) throw Conflict("SOURCE_REQUEST_INCOMPLETE", "Confirm every device's internal use first.");
                }
                result = request.ServiceEvidenceJson is { } evidence && ProcurementJson.Read<ServiceEvidenceLine[]>(evidence).Any(l => l.Outcome != ServiceEvidenceOutcome.Completed) ? "CompletedWithIssues" : "Success";
            }
        return result;
    }
}
