using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CSVC.Core.Accounts;
using CSVC.Core.Common;
using CSVC.Core.Inventories;
using CSVC.UseCases.Abstractions.Persistence;
using CSVC.UseCases.Abstractions.Security;
using CSVC.UseCases.Exceptions;
using CSVC.UseCases.Settings;

namespace CSVC.UseCases.Inventories;

public sealed partial class InventoryService(IEquipmentInventoryRepository repo, IUnitOfWork uow,
    IInventoryTransaction transaction, ICurrentAccount current, TimeProvider clock, IEquipmentDepartmentPolicy equipmentPolicy,
    IRepository<InventorySlip> slips, IRepository<InventoryPlanRevision> revisions,
    IRepository<InventoryResult> results, IRepository<InventoryHistory> history,
    IRepository<InventoryItemHistory> itemHistory, IRepository<InventoryNoticePolicy> policies,
    IRepository<InventoryNoticePolicyHistory> policyHistory, IRepository<InventoryNotification> notifications) : IInventoryService
{
    private sealed record Actor(Guid Id, string Code, string Name, Guid DepartmentId, string Role)
    {
        public bool IsEquipment => Role is RoleCodes.EquipmentManager or RoleCodes.EquipmentStaff;
        public bool IsManager => Role == RoleCodes.EquipmentManager;
    }
    private async Task<Actor> ActorAsync(CancellationToken ct)
    {
        if (current.AccountId is not { } id || current.SessionId is not { } session) throw Unauthorized();
        var account = await uow.Accounts.FirstOrDefaultAsync(x => x.Id == id && !x.IsLock, ct) ?? throw Unauthorized();
        if (!await uow.AuthSessions.ExistsAsync(x => x.Id == session && x.AccountId == id && x.IsActive && x.ExpiresAt > clock.GetUtcNow(), ct)) throw Unauthorized();
        var profile = await uow.UserProfiles.FirstOrDefaultAsync(x => x.Id == account.UserProfileId, ct) ?? throw Unauthorized();
        if (!await uow.Departments.ExistsAsync(x => x.Id == profile.DepartmentId && x.Status == EntityStatus.Active, ct)) throw Forbidden();
        var role = await uow.Roles.FirstOrDefaultAsync(x => x.Id == account.RoleId, ct);
        if (role?.Code is not (RoleCodes.EquipmentManager or RoleCodes.EquipmentStaff or RoleCodes.DepartmentStaff)) throw Forbidden();
        if (role.Code != RoleCodes.DepartmentStaff) await equipmentPolicy.RequireMemberAsync(role.Code, profile.DepartmentId, ct);
        return new(id, account.AccountCode, profile.FullName, profile.DepartmentId, role.Code);
    }
    private Task<T> CommandAsync<T>(Guid requestId, string action, object payload, bool manager,
        Func<Actor, string, CancellationToken, Task<T>> execute, CancellationToken ct)
        => transaction.ExecuteAsync(async token =>
        {
            var actor = await ActorAsync(token); Equipment(actor); if (manager) Manager(actor);
            if (requestId == Guid.Empty) throw Invalid("REQUEST_ID_REQUIRED", "A nonempty request ID is required.");
            Validate(payload);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { action, payload }))));
            if (await repo.RequestAsync(requestId, token) is { } prior)
            {
                if (prior.ActorId != actor.Id || prior.Action != action || prior.Hash != hash)
                    throw Conflict("IDEMPOTENCY_CONFLICT", "The request ID was already used for a different actor or payload.");
                return JsonSerializer.Deserialize<T>(prior.Response)!;
            }
            return await execute(actor, hash, token);
        }, ct);

    private Task<InventorySlipDto> ChangeAsync(Guid id, InventoryCommand request, string action, object payload,
        bool manager, Func<InventorySlip, Actor, CancellationToken, Task> change, CancellationToken ct)
        => CommandAsync(request.RequestId, action, new { id, request = payload }, manager, async (actor, hash, token) =>
        {
            Validate(request);
            var slip = await LoadAsync(id, actor, true, token);
            Version(slip.Version, request.ExpectedVersion);
            var before = JsonSerializer.Serialize(Detail(slip));
            await change(slip, actor, token);
            slip.Version++;
            var dto = Detail(slip);
            await AuditAsync(slip, actor, request.RequestId, hash, action, before, dto, token);
            return dto;
        }, ct);

    public Task<InventorySlipDto> CreateAsync(CreateInventoryRequest request, CancellationToken ct)
        => CommandAsync(request.RequestId, "CREATE", request, false, async (actor, hash, token) =>
        {
            var department = await DepartmentAsync(request.DepartmentId, token);
            var now = clock.GetUtcNow();
            var slip = new InventorySlip { DisplayCode = BusinessCode.NewRandom("KK"), CreateRequestId = request.RequestId,
                DepartmentId = department.Id, DepartmentCodeSnapshot = department.Code, DepartmentNameSnapshot = department.Name,
                CreatedByAccountId = actor.Id, CreatorCodeSnapshot = actor.Code, CreatorNameSnapshot = actor.Name,
                CreatedAt = now, Notes = Clean(request.Notes) };
            var revision = await BuildRevisionAsync(slip, 1, request.PlannedStartAt, request.PlannedEndAt,
                request.DeviceIds, request.AssigneeAccountIds, null, actor, token);
            slip.Revisions.Add(revision);
            await slips.AddAsync(slip, token);
            var dto = Detail(slip);
            await AuditAsync(slip, actor, request.RequestId, hash, "CREATE", null, dto, token);
            return dto;
        }, ct);

    public Task<InventorySlipDto> UpdateAsync(Guid id, UpdateInventoryPlanRequest request, CancellationToken ct)
        => ChangeAsync(id, request, "UPDATE_PLAN", request, false, async (slip, actor, token) =>
        {
            State(slip, InventoryStates.Pending); Owner(slip, actor); Validate(request);
            var department = await DepartmentAsync(request.DepartmentId, token);
            slip.DepartmentId = department.Id; slip.DepartmentCodeSnapshot = department.Code; slip.DepartmentNameSnapshot = department.Name;
            var draft = slip.Revisions.Single(x => x.Status == InventoryStates.Draft);
            var replacement = await BuildRevisionAsync(slip, draft.RevisionNo, request.PlannedStartAt, request.PlannedEndAt,
                request.DeviceIds, request.AssigneeAccountIds, null, actor, token);
            foreach (var i in replacement.Items) i.RevisionId = draft.Id;
            foreach (var a in replacement.Assignees) a.RevisionId = draft.Id;
            repo.ReplaceDraft(draft, replacement.Items, replacement.Assignees);
            draft.PlannedStartAt = replacement.PlannedStartAt; draft.PlannedEndAt = replacement.PlannedEndAt;
            draft.Version++; draft.ProposedByAccountId = actor.Id; draft.ProposedAt = clock.GetUtcNow();
            slip.Notes = Clean(request.Notes);
        }, ct);

    public Task<InventorySlipDto> ProposeAsync(Guid id, UpdateInventoryPlanRequest request, CancellationToken ct)
        => ChangeAsync(id, request, "PROPOSE_PLAN", request, false, async (slip, actor, token) =>
        {
            State(slip, InventoryStates.Preparing); Validate(request);
            if (request.DepartmentId != slip.DepartmentId) throw Invalid("DEPARTMENT_IMMUTABLE", "A published slip cannot change department.");
            var reason = Reason(request.Reason);
            if (Clean(request.Notes) != slip.Notes) throw Invalid("PUBLISHED_NOTES_IMMUTABLE", "Published header notes cannot be changed by a plan revision.");
            if (slip.Revisions.Any(x => x.Status == InventoryStates.Draft)) throw Conflict("DRAFT_ALREADY_EXISTS", "Publish or reject the pending revision first.");
            var previous = Active(slip);
            var revision = await BuildRevisionAsync(slip, slip.Revisions.Max(x => x.RevisionNo) + 1,
                request.PlannedStartAt, request.PlannedEndAt, request.DeviceIds, request.AssigneeAccountIds, reason, actor, token);
            revision.DiffData = JsonSerializer.Serialize(new { before = RevisionDto(previous), after = RevisionDto(revision) });
            slip.Revisions.Add(revision); await revisions.AddAsync(revision, token);
        }, ct);

    public Task<InventorySlipDto> PublishAsync(Guid id, Guid revisionId, InventoryCommand request, CancellationToken ct)
        => ChangeAsync(id, request, "PUBLISH", new { revisionId, request }, true, async (slip, actor, token) =>
        {
            if (slip.Status is not (InventoryStates.Pending or InventoryStates.Preparing)) State(slip, InventoryStates.Pending);
            var revision = slip.Revisions.SingleOrDefault(x => x.Id == revisionId) ?? throw Missing();
            if (revision.Status != InventoryStates.Draft) throw Conflict("REVISION_NOT_DRAFT", "Only a draft revision can be published.");
            if (revision.Items.Count == 0 || revision.Assignees.Count == 0) throw Invalid("PLAN_EMPTY", "Items and assignees are required.");
            await DepartmentAsync(slip.DepartmentId, token);
            await transaction.LockDevicesAsync(revision.Items.Select(x => x.DeviceId), token);
            foreach (var item in revision.Items)
            {
                var device = await uow.Devices.FirstOrDefaultAsync(x => x.Id == item.DeviceId, token) ?? throw Missing();
                if (device.DepartmentId != slip.DepartmentId || device.DeviceCode != item.DeviceCodeSnapshot || device.Name != item.DeviceNameSnapshot
                    || device.SerialNumber != item.SerialSnapshot || device.Quantity != item.QuantitySnapshot || device.AreaId != item.AreaIdSnapshot
                    || await AreaPathAsync(device.AreaId, token) != item.AreaPathSnapshot)
                    throw Conflict("PLAN_SOURCE_CHANGED", "Device data changed. Refresh the draft before publishing.");
            }
            foreach (var assignee in revision.Assignees) await AssigneeAsync(assignee.AccountId, token);
            var policy = await PolicyEntityAsync(false, token); var now = clock.GetUtcNow();
            if (revision.PlannedStartAt < now.AddDays(policy.MinimumNoticeDays)) throw Conflict("NOTICE_TOO_SHORT", "The plan does not meet the configured notice period.");
            if (await repo.OverlapsAsync(slip.Id, slip.DepartmentId, revision.PlannedStartAt, revision.PlannedEndAt, token))
                throw Conflict("SCHEDULE_CONFLICT", "Another published inventory overlaps this department's schedule.");
            revision.Status = InventoryStates.Published; revision.ApprovedAt = now; revision.ApprovedByAccountId = actor.Id;
            revision.PublishedAt = now; revision.NoticeDaysSnapshot = policy.MinimumNoticeDays; revision.Version++;
            await uow.CommitAsync(token);
            var initial = slip.Status == InventoryStates.Pending;
            slip.Status = InventoryStates.Preparing; slip.ApprovedByAccountId ??= actor.Id; slip.ApprovedAt ??= now;
            await NotifyAsync(slip, revision, initial ? "PLAN_PUBLISHED" : "PLAN_UPDATED", now, true, token);
        }, ct);

    public Task<InventorySlipDto> RejectRevisionAsync(Guid id, Guid revisionId, InventoryReasonRequest request, CancellationToken ct)
        => ChangeAsync(id, request, "REJECT_PLAN", new { revisionId, request }, true, (slip, actor, _) =>
        {
            State(slip, InventoryStates.Preparing);
            var revision = slip.Revisions.SingleOrDefault(x => x.Id == revisionId && x.Status == InventoryStates.Draft) ?? throw Missing();
            revision.Status = InventoryStates.Rejected; revision.RejectedByAccountId = actor.Id;
            revision.RejectedAt = clock.GetUtcNow(); revision.RejectionReason = Reason(request.Reason); revision.Version++;
            return Task.CompletedTask;
        }, ct);

    public Task<InventorySlipDto> CancelAsync(Guid id, InventoryReasonRequest request, CancellationToken ct)
        => ChangeAsync(id, request, "CANCEL", request, false, async (slip, actor, token) =>
        {
            if (slip.Status == InventoryStates.Pending) Owner(slip, actor);
            else { State(slip, InventoryStates.Preparing); Manager(actor); }
            slip.CancelReason = Reason(request.Reason); slip.CanceledByAccountId = actor.Id; slip.CanceledAt = clock.GetUtcNow();
            if (slip.Status == InventoryStates.Preparing) await NotifyAsync(slip, Active(slip), "PLAN_CANCELED", clock.GetUtcNow(), false, token);
            slip.Status = InventoryStates.Canceled;
        }, ct);

    public Task<InventorySlipDto> StartAsync(Guid id, InventoryCommand request, CancellationToken ct)
        => ChangeAsync(id, request, "START", request, false, (slip, actor, _) =>
        {
            State(slip, InventoryStates.Preparing); var revision = Active(slip); Assigned(revision, actor);
            if (slip.Revisions.Any(x => x.Status == InventoryStates.Draft)) throw Conflict("PLAN_CHANGE_PENDING", "Publish or reject the pending revision before starting.");
            var now = clock.GetUtcNow();
            if (now < revision.PlannedStartAt || revision.PublishedAt is not { } published || revision.NoticeDaysSnapshot is not { } days || now < published.AddDays(days))
                throw Conflict("START_TOO_EARLY", "The scheduled start and notice period must both be reached.");
            slip.Status = InventoryStates.InProgress; slip.StartedByAccountId = actor.Id; slip.StartedAt = now;
            return Task.CompletedTask;
        }, ct);

    public Task<InventorySlipDto> CompleteAsync(Guid id, InventoryCommand request, CancellationToken ct)
        => ChangeAsync(id, request, "COMPLETE", request, true, (slip, actor, _) =>
        {
            State(slip, InventoryStates.InProgress); var progress = Progress(Active(slip));
            if (progress.Total == 0 || progress.Unchecked != 0) throw Conflict("INVENTORY_HAS_UNCHECKED_ITEMS", "Every item must have a result before completion.");
            slip.Status = InventoryStates.Completed; slip.CompletedByAccountId = actor.Id; slip.CompletedAt = clock.GetUtcNow();
            return Task.CompletedTask;
        }, ct);

    private async Task<InventoryPlanRevision> BuildRevisionAsync(InventorySlip slip, int number, DateTimeOffset start,
        DateTimeOffset? end, List<Guid>? deviceIds, List<Guid> accountIds, string? reason, Actor actor, CancellationToken ct)
    {
        if (start <= clock.GetUtcNow() || end is { } finish && finish <= start) throw Invalid("INVALID_SCHEDULE", "Use a future start and an end later than the start.");
        deviceIds ??= (await uow.Devices.FindAsync(x => x.DepartmentId == slip.DepartmentId, ct)).Select(x => x.Id).ToList();
        Ids(deviceIds, 2000); Ids(accountIds, 100);
        await transaction.LockDevicesAsync(deviceIds, ct);
        var revision = new InventoryPlanRevision { SlipId = slip.Id, RevisionNo = number, PlannedStartAt = start.ToUniversalTime(),
            PlannedEndAt = end?.ToUniversalTime(), ChangeReason = reason, ProposedByAccountId = actor.Id, ProposedAt = clock.GetUtcNow() };
        foreach (var id in deviceIds.Order())
        {
            var device = await uow.Devices.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing();
            if (device.DepartmentId != slip.DepartmentId) throw Invalid("DEVICE_DEPARTMENT_MISMATCH", "Every device must belong to the selected department.");
            revision.Items.Add(new() { RevisionId = revision.Id, DeviceId = device.Id, DeviceCodeSnapshot = device.DeviceCode,
                DeviceNameSnapshot = device.Name, SerialSnapshot = device.SerialNumber, QuantitySnapshot = device.Quantity,
                DepartmentIdSnapshot = device.DepartmentId, AreaIdSnapshot = device.AreaId,
                AreaPathSnapshot = await AreaPathAsync(device.AreaId, ct), CapturedAt = clock.GetUtcNow() });
        }
        foreach (var id in accountIds.Order())
        {
            var assignee = await AssigneeAsync(id, ct);
            revision.Assignees.Add(new() { RevisionId = revision.Id, AccountId = id, AccountCodeSnapshot = assignee.Code,
                NameSnapshot = assignee.Name, AssignedByAccountId = actor.Id, AssignedAt = clock.GetUtcNow() });
        }
        return revision;
    }
    private async Task<(string Code, string Name)> AssigneeAsync(Guid id, CancellationToken ct)
    {
        var account = await uow.Accounts.FirstOrDefaultAsync(x => x.Id == id && !x.IsLock, ct) ?? throw Invalid("ASSIGNEE_UNAVAILABLE", "An assignee is unavailable.");
        var role = await uow.Roles.FirstOrDefaultAsync(x => x.Id == account.RoleId, ct);
        if (role?.Code is not (RoleCodes.EquipmentManager or RoleCodes.EquipmentStaff)) throw Invalid("ASSIGNEE_NOT_QTTB", "Assign only QTTB accounts.");
        var profile = await uow.UserProfiles.FirstOrDefaultAsync(x => x.Id == account.UserProfileId, ct) ?? throw Missing();
        await equipmentPolicy.RequireMemberAsync(role.Code, profile.DepartmentId, ct);
        return (account.AccountCode, profile.FullName);
    }
    private async Task<string?> AreaPathAsync(Guid? id, CancellationToken ct)
    {
        var names = new List<string>(); var seen = new HashSet<Guid>();
        while (id is { } areaId)
        {
            if (!seen.Add(areaId)) throw Conflict("AREA_STATE_INVALID", "The area hierarchy contains a cycle.");
            var area = await uow.Areas.FirstOrDefaultAsync(x => x.Id == areaId, ct) ?? throw Missing();
            names.Add(area.Name); id = area.ParentId;
        }
        names.Reverse(); return names.Count == 0 ? null : string.Join(" / ", names);
    }
    private async Task<CSVC.Core.Departments.Department> DepartmentAsync(Guid id, CancellationToken ct)
        => await uow.Departments.FirstOrDefaultAsync(x => x.Id == id && x.Status == EntityStatus.Active, ct)
            ?? throw Invalid("DEPARTMENT_UNAVAILABLE", "Select an active department.");
    private Task AuditAsync(InventorySlip slip, Actor actor, Guid requestId, string hash, string action,
        string? before, InventorySlipDto dto, CancellationToken ct)
        => history.AddAsync(new InventoryHistory { SlipId = slip.Id, RequestId = requestId, RequestHash = hash,
            ResponseData = JsonSerializer.Serialize(dto), TargetKey = action is "PROPOSE_PLAN" or "PUBLISH" or "REJECT_PLAN" or "UPDATE_PLAN"
                ? "REVISION:" + slip.Revisions.MaxBy(x => x.RevisionNo)!.Id : "SLIP",
            ActionCode = action, ActorAccountId = actor.Id, OccurredAt = clock.GetUtcNow(), BeforeData = before,
            AfterData = JsonSerializer.Serialize(dto), ReasonCode = slip.CancelReason }, ct);
    private static void Validate(object value)
    {
        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(value, new ValidationContext(value), errors, true))
            throw Invalid("VALIDATION_FAILED", errors[0].ErrorMessage ?? "Invalid input.");
    }
    private static void Ids(List<Guid>? ids, int maximum)
    {
        if (ids is null || ids.Count > maximum || ids.Contains(Guid.Empty) || ids.Distinct().Count() != ids.Count)
            throw Invalid("INVALID_SELECTION", "Select a nonempty list of distinct IDs within the allowed limit.");
    }
    private static void Version(int currentVersion, int? expected)
    {
        if (expected is null || expected < 0) throw Invalid("EXPECTED_VERSION_REQUIRED", "A nonnegative expected version is required.");
        if (currentVersion != expected) throw Conflict("VERSION_CONFLICT", "The data changed. Reload before retrying.");
    }
    private static void State(InventorySlip slip, string state)
    {
        if (slip.Status == InventoryStates.Completed) throw Conflict("INVENTORY_COMPLETED", "The completed inventory is immutable.");
        if (slip.Status != state) throw Conflict("INVALID_INVENTORY_STATE", "The operation is unavailable in this state.");
    }
    private static void Equipment(Actor actor) { if (!actor.IsEquipment) throw Forbidden(); }
    private static void Manager(Actor actor) { if (!actor.IsManager) throw Forbidden(); }
    private static void Owner(InventorySlip slip, Actor actor) { if (!actor.IsManager && slip.CreatedByAccountId != actor.Id) throw Forbidden(); }
    private static void Assigned(InventoryPlanRevision revision, Actor actor)
    { Equipment(actor); if (!revision.Assignees.Any(x => x.AccountId == actor.Id)) throw Forbidden(); }
    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    private static string Reason(string? reason) => Clean(reason) is { Length: <= 2000 } value ? value : throw Invalid("REASON_REQUIRED", "A reason of at most 2000 characters is required.");
    private static UseCaseException Invalid(string code, string message) => new(FailureKind.Validation, code, message);
    private static UseCaseException Conflict(string code, string message) => new(FailureKind.Conflict, code, message);
    private static UseCaseException Missing() => new(FailureKind.NotFound, "INVENTORY_NOT_FOUND", "The inventory resource is unavailable.");
    private static UseCaseException Forbidden() => new(FailureKind.Forbidden, "INVENTORY_FORBIDDEN", "You cannot perform this inventory operation.");
    private static UseCaseException Unauthorized() => new(FailureKind.Unauthorized, "AUTHENTICATION_REQUIRED", "A valid session is required.");
}
