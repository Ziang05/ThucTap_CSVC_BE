using System.Security.Cryptography;
using System.Text;
using CSVC.Core.Accounts;
using CSVC.Core.Common;
using CSVC.Core.ProcurementRequests;
using CSVC.UseCases.Exceptions;

namespace CSVC.UseCases.ProcurementRequests;

public sealed partial class ProcurementRequestService
{
    private sealed record Reader(Guid Id, string Role, string DepartmentCode);
    private async Task<Reader> ReadActorAsync(CancellationToken ct)
    {
        if (current.AccountId is not { } id || current.SessionId is not { } session) throw Unauthorized();
        var account = await uow.Accounts.FirstOrDefaultAsync(a => a.Id == id && !a.IsLock, ct) ?? throw Unauthorized();
        if (!await uow.AuthSessions.ExistsAsync(s => s.Id == session && s.AccountId == id && s.IsActive && s.ExpiresAt > clock.GetUtcNow(), ct)) throw Unauthorized();
        var role = await uow.Roles.GetByIdAsync(account.RoleId, ct);
        if (role?.Code is not (RoleCodes.EquipmentManager or RoleCodes.EquipmentStaff or RoleCodes.DepartmentStaff)) throw ForbiddenSource();
        var profile = await uow.UserProfiles.GetByIdAsync(account.UserProfileId, ct) ?? throw Unauthorized();
        var department = await uow.Departments.GetByIdAsync(profile.DepartmentId, ct) ?? throw Unauthorized();
        if (department.Status != EntityStatus.Active) throw ForbiddenSource();
        if (role.Code != RoleCodes.DepartmentStaff) await equipment.RequireMemberAsync(role.Code, department.Id, ct);
        return new(id, role.Code, department.Code);
    }
    private static void RequireVisible(ProcurementRequest request, Reader actor)
    {
        if (actor.Role == RoleCodes.EquipmentStaff && request.AssigneeAccountId != actor.Id) throw Missing();
        if (actor.Role == RoleCodes.DepartmentStaff && request.RequestingDepartmentCode != actor.DepartmentCode) throw Missing();
    }
    private static bool IsReceiver(ProcurementRequest request, Reader actor)
        => actor.DepartmentCode == request.RequestingDepartmentCode && actor.Role is RoleCodes.DepartmentStaff or RoleCodes.EquipmentStaff;
    private static bool NeedsAcceptance(ProcurementRequest request)
        => request.Lines.Any(l => l.Kind == ProcurementLineKind.Service) || (request.RequestType == ProcurementRequestType.Repair && request.WorkflowVersion >= 2);
    private static UseCaseException ForbiddenSource() => new(FailureKind.Forbidden, "SOURCE_REQUEST_FORBIDDEN", "This request action is not permitted for this account.");
    private static string CommandHash(string code, object input)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ProcurementJson.Write(new { code, input }))));
    private async Task<bool> ReplayCommandAsync(ProcurementRequest request, Guid commandId, Guid actor, string hash, CancellationToken ct)
    {
        if (commandId == Guid.Empty) throw Invalid("A nonempty requestId is required.");
        var prior = await events.FirstOrDefaultAsync(e => e.CommandId == commandId, ct);
        if (prior is null) return false;
        if (prior.RequestId != request.Id || prior.ActorAccountId != actor || prior.PayloadHash != hash)
            throw Conflict("IDEMPOTENCY_CONFLICT", "requestId was already used for different content or a different actor.");
        return true;
    }
    private async Task CommandEventAsync(ProcurementRequest request, Guid commandId, Guid actor, string hash,
        string action, string outcome, object payload, CancellationToken ct)
        => await events.AddAsync(new ProcurementRequestEvent
        {
            RequestId = request.Id, CommandId = commandId, ActorAccountId = actor, PayloadHash = hash,
            Action = action, Outcome = outcome, Payload = ProcurementJson.Write(payload), OccurredAt = clock.GetUtcNow()
        }, ct);

    public Task RecordServicesAsync(string code, RecordServiceEvidence input, CancellationToken ct)
        => transaction.ExecuteAsync(async token =>
        {
            var actor = await ReadActorAsync(token);
            if (actor.Role != RoleCodes.EquipmentStaff) throw ForbiddenSource();
            Validate(input);
            var request = await requests.GetAsync(code, true, token) ?? throw Missing();
            RequireVisible(request, actor);
            var hash = CommandHash(request.DisplayCode, input);
            if (await ReplayCommandAsync(request, input.RequestId, actor.Id, hash, token)) return true;
            await EditableAsync(code, input.Version, token);
            await assignment.RequireAsync(code, actor.Id, token);
            if (request.RequiresReview || request.MappingIssue is not null) throw Conflict("SOURCE_REQUEST_NOT_READY", "Resolve source mapping or changes first.");
            if (request.AcceptanceDecision == "Accepted") throw Conflict("RESULT_ALREADY_ACCEPTED", "Accepted evidence is immutable.");
            var lines = request.Lines.Where(l => l.Kind == ProcurementLineKind.Service).ToArray();
            if (lines.Length == 0 || input.Lines.Any(l => l is null)) throw Invalid("Select a request with service lines and provide their complete results.");
            foreach (var line in input.Lines) Validate(line);
            var normalized = input.Lines.Select(l => new ServiceEvidenceLine
            {
                SourceLineCode = BusinessCode.Normalize(l.SourceLineCode) ?? "", Outcome = l.Outcome,
                Provider = TextNormalization.Name(l.Provider), EvidenceReference = TextNormalization.Name(l.EvidenceReference),
                Description = TextNormalization.Name(l.Description), PerformedAt = l.PerformedAt?.ToUniversalTime() ?? clock.GetUtcNow()
            }).OrderBy(l => l.SourceLineCode, StringComparer.Ordinal).ToArray();
            if (normalized.Length != lines.Length || normalized.Select(l => l.SourceLineCode).Distinct().Count() != lines.Length
                || lines.Any(l => !normalized.Any(i => i.SourceLineCode == l.SourceLineCode))) throw Invalid("Record every service line exactly once; partial service fulfillment is not allowed.");
            if (normalized.Any(l => l.PerformedAt > clock.GetUtcNow() || l.PerformedAt < request.ReceivedAt || l.Provider.Length == 0 || l.EvidenceReference.Length == 0 || l.Description.Length == 0))
                throw Invalid("Provide the performer, evidence reference, description and a valid performance time for every service line.");
            var before = request.ServiceEvidenceJson;
            request.ServiceEvidenceJson = ProcurementJson.Write(normalized);
            request.ServiceRecordedByAccountId = actor.Id; request.ServiceRecordedAt = clock.GetUtcNow();
            request.AcceptanceDecision = null; request.AcceptanceReason = null; request.AcceptanceScopeHash = null;
            request.AcceptedByAccountId = null; request.AcceptedAt = null;
            if (request.Status == ProcurementRequestStatus.Received) request.Status = ProcurementRequestStatus.InProgress;
            request.Version++;
            await CommandEventAsync(request, input.RequestId, actor.Id, hash, "SERVICE_EVIDENCE_RECORDED", "Recorded", new { before, lines = normalized }, token);
            return true;
        }, ct);

    private static void RequireServiceEvidence(ProcurementRequest request)
    {
        var required = request.Lines.Where(l => l.Kind == ProcurementLineKind.Service).ToArray();
        if (required.Length == 0) return;
        if (request.ServiceEvidenceJson is null) throw Conflict("SERVICE_EVIDENCE_REQUIRED", "Record the complete service fulfillment evidence first.");
        var evidence = ProcurementJson.Read<ServiceEvidenceLine[]>(request.ServiceEvidenceJson);
        if (evidence.Length != required.Length || required.Any(l => !evidence.Any(e => e.SourceLineCode == l.SourceLineCode)))
            throw Conflict("SERVICE_EVIDENCE_REQUIRED", "Service evidence does not cover the current source scope.");
    }
    private async Task<string> CompletionHashAsync(ProcurementRequest request, CancellationToken ct)
    {
        var documents = (await workflow.DocumentsAsync(request, ct)).Where(d => d.Kind != RequestDocumentKind.ServiceEvidence)
            .OrderBy(d => d.Kind).ThenBy(d => d.DocumentCode).ToArray();
        var repairs = new List<object>();
        foreach (var document in documents.Where(d => d.Kind == RequestDocumentKind.Repair))
        {
            var order = (await services.GetAggregateAsync(document.DocumentCode, false, ct))!;
            repairs.Add(new { order.DisplayCode, items = order.Items.OrderBy(i => i.ItemNo).Select(i => new { i.ItemNo, i.ExecutionStatus, i.ResultDescription, i.ResultRecordedAt }).ToArray() });
        }
        return CommandHash(request.DisplayCode, new
        {
            request.RequestType, request.RequestingDepartmentCode,
            lines = request.Lines.OrderBy(l => l.SourceLineCode).Select(l => new { l.SourceLineCode, l.Kind, l.Quantity, l.Unit, l.DeviceCode }).ToArray(),
            request.ServiceEvidenceJson, documents, repairs
        });
    }
    public Task AcceptResultsAsync(string code, AcceptRequestResults input, CancellationToken ct)
        => transaction.ExecuteAsync(async token =>
        {
            var actor = await ReadActorAsync(token); Validate(input);
            var request = await requests.GetAsync(code, true, token) ?? throw Missing();
            RequireVisible(request, actor);
            if (!IsReceiver(request, actor)) throw ForbiddenSource();
            var hash = CommandHash(request.DisplayCode, input);
            if (await ReplayCommandAsync(request, input.RequestId, actor.Id, hash, token)) return true;
            await EditableAsync(code, input.Version, token);
            if (!NeedsAcceptance(request)) throw Invalid("This request is confirmed through its allocation receipt operation.");
            if (request.RequiresReview || request.MappingIssue is not null) throw Conflict("SOURCE_REQUEST_NOT_READY", "Resolve source changes first.");
            if (request.AcceptanceDecision is not null) throw Conflict("RESULT_ALREADY_REVIEWED", "Record corrected service evidence before another acceptance decision.");
            if (!input.Accepted && string.IsNullOrWhiteSpace(input.Reason)) throw Invalid("A rejection reason is required.");
            _ = await ValidateCompletionAsync(request, null, token);
            request.AcceptanceDecision = input.Accepted ? "Accepted" : "Rejected";
            request.AcceptanceReason = string.IsNullOrWhiteSpace(input.Reason) ? null : TextNormalization.Name(input.Reason);
            request.AcceptanceScopeHash = await CompletionHashAsync(request, token);
            request.AcceptedByAccountId = actor.Id; request.AcceptedAt = clock.GetUtcNow(); request.Version++;
            await CommandEventAsync(request, input.RequestId, actor.Id, hash, "RESULT_ACCEPTANCE_RECORDED", request.AcceptanceDecision,
                new { request.AcceptanceReason, request.AcceptanceScopeHash }, token);
            return true;
        }, ct);
}
