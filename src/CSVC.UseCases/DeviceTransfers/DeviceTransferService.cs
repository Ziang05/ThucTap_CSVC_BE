using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.RegularExpressions;
using CSVC.Core.Accounts;
using CSVC.Core.Common;
using CSVC.Core.Departments;
using CSVC.Core.DeviceTransfers;
using CSVC.Core.Devices;
using CSVC.UseCases.Abstractions;
using CSVC.UseCases.Abstractions.Persistence;
using CSVC.UseCases.Abstractions.Security;
using CSVC.UseCases.Common;
using CSVC.UseCases.DeviceTransfers.DTOs;
using CSVC.UseCases.DeviceTransfers.Models;
using CSVC.UseCases.Exceptions;

namespace CSVC.UseCases.DeviceTransfers;

public sealed class DeviceTransferService(
    IUnitOfWork unitOfWork, IInventoryTransaction transaction, ICurrentAccount current,
    IRequestContext requestContext, TimeProvider clock) : IDeviceTransferService
{
    private static readonly Regex DeviceCodePattern = new("^[A-Z0-9][A-Z0-9._/-]{0,31}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public async Task<PagedResult<DeviceTransferDto>> ListAsync(DeviceTransferListQuery query, CancellationToken ct)
    {
        var actor = await RequireActorAsync(ct);
        Validate(query);
        if (query.FromDate > query.ToDate) throw Validation("fromDate cannot be later than toDate.");
        Guid? scope = actor.RoleCode == RoleCodes.DepartmentStaff ? actor.DepartmentId : null;
        var page = await unitOfWork.DeviceTransfers.ListAsync(query, scope, actor.AccountId, ct);
        return new(page.Items.Select(ToDto).ToArray(), page.TotalCount, page.Page, page.PageSize);
    }

    public async Task<DeviceTransferDto> GetAsync(string transferCode, CancellationToken ct)
    {
        var actor = await RequireActorAsync(ct);
        var transfer = await GetScopedAsync(transferCode, actor, ct);
        return await ToDetailAsync(transfer, ct);
    }

    public async Task<DeviceTransferDto> CreateAsync(CreateDeviceTransferRequest request, CancellationToken ct)
        => await transaction.ExecuteAsync(async token =>
        {
            var actor = await RequireDepartmentStaffAsync(token);
            Validate(request);
            var (source, target, responsible, reason, devices) = await ValidateTransferRequestAsync(request, actor, [], token);
            var transfer = new DeviceTransfer
            {
                TransferCode = "DC" + Guid.NewGuid().ToString("N").ToUpperInvariant(),
                SourceDepartmentId = source.Id, TargetDepartmentId = target.Id,
                TargetResponsibleAccountId = responsible.Id, Reason = reason, CreatorAccountId = actor.AccountId,
                CreatedAt = clock.GetUtcNow(), Status = DeviceTransferStatus.PendingApproval, Notes = Clean(request.Notes),
                Items = devices.Select(device => new DeviceTransferItem { DeviceId = device.Id }).ToList()
            };
            await unitOfWork.DeviceTransfers.AddAsync(transfer, token);
            await AuditAsync(transfer, actor.AccountId, "TRANSFER_CREATED", ["transferCode", "sourceDepartmentCode", "targetDepartmentCode", "targetResponsibleAccountCode", "reason", "items"],
                null, Snapshot(transfer, source.Code, target.Code, responsible.AccountCode, devices.Select(x => new { x.DeviceCode, x.Name })), token);
            await unitOfWork.CommitAsync(token);
            var read = await unitOfWork.DeviceTransfers.GetByCodeAsync(transfer.TransferCode, actor.DepartmentId, actor.AccountId, token) ?? throw Missing();
            return await ToDetailAsync(read, token);
        }, ct);

    public async Task UpdateAsync(string transferCode, UpdateDeviceTransferRequest request, CancellationToken ct)
        => await transaction.ExecuteAsync(async token =>
        {
            var actor = await RequireDepartmentStaffAsync(token);
            Validate(request);
            var transfer = await unitOfWork.DeviceTransfers.FirstOrDefaultTrackedAsync(x => x.TransferCode == NormalizeCode(transferCode), token) ?? throw Missing();
            RequirePending(transfer);
            if (transfer.CreatorAccountId != actor.AccountId) throw Forbidden("Only the transfer creator may edit it.");
            var oldItems = await unitOfWork.DeviceTransferItems.ListTrackedForTransferAsync(transfer.Id, token);
            var oldDevices = await LoadTransferDevicesAsync(oldItems, token);
            var sourceBefore = await DepartmentCodeAsync(transfer.SourceDepartmentId, token);
            var targetBefore = await DepartmentCodeAsync(transfer.TargetDepartmentId, token);
            var responsibleBefore = await AccountCodeAsync(transfer.TargetResponsibleAccountId, token);
            var before = Snapshot(transfer, sourceBefore, targetBefore, responsibleBefore, oldDevices.Select(x => new { x.DeviceCode, x.Name }));
            var (source, target, responsible, reason, devices) = await ValidateTransferRequestAsync(request, actor, oldDevices, token);
            var retainedIds = devices.Select(x => x.Id).ToHashSet();
            foreach (var old in oldItems.Where(x => !retainedIds.Contains(x.DeviceId))) await unitOfWork.DeviceTransferItems.DeleteAsync(old, token);
            var existingIds = oldItems.Select(x => x.DeviceId).ToHashSet();
            foreach (var device in devices.Where(x => !existingIds.Contains(x.Id)))
                await unitOfWork.DeviceTransferItems.AddAsync(new DeviceTransferItem { TransferId = transfer.Id, DeviceId = device.Id }, token);
            transfer.SourceDepartmentId = source.Id;
            transfer.TargetDepartmentId = target.Id;
            transfer.TargetResponsibleAccountId = responsible.Id;
            transfer.Reason = reason;
            transfer.Notes = Clean(request.Notes);
            unitOfWork.DeviceTransfers.Update(transfer);
            await AuditAsync(transfer, actor.AccountId, "TRANSFER_UPDATED", ["sourceDepartmentCode", "targetDepartmentCode", "targetResponsibleAccountCode", "reason", "items", "notes"],
                before, Snapshot(transfer, source.Code, target.Code, responsible.AccountCode, devices.Select(x => new { x.DeviceCode, x.Name })), token);
            await unitOfWork.CommitAsync(token);
            return true;
        }, ct);

    public async Task DecideAsync(string transferCode, bool approve, TransferDecisionRequest request, CancellationToken ct)
        => await transaction.ExecuteAsync(async token =>
        {
            var actor = await RequireEquipmentAsync(token);
            Validate(request);
            var transfer = await unitOfWork.DeviceTransfers.FirstOrDefaultTrackedAsync(x => x.TransferCode == NormalizeCode(transferCode), token) ?? throw Missing();
            RequirePending(transfer);
            var items = await unitOfWork.DeviceTransferItems.ListTrackedForTransferAsync(transfer.Id, token);
            if (items.Count == 0) throw Conflict("TRANSFER_EMPTY", "A transfer must contain at least one device.");
            var before = SnapshotStatus(transfer);
            if (approve)
            {
                var responsible = await GetTargetResponsibleAsync(transfer.TargetResponsibleAccountId, transfer.TargetDepartmentId, token);
                if (responsible.IsLock) throw Conflict("TARGET_RESPONSIBLE_UNAVAILABLE", "The target responsible account is locked.");
                var source = await unitOfWork.Departments.GetByIdAsync(transfer.SourceDepartmentId, token) ?? throw Conflict("DEPARTMENT_UNAVAILABLE", "The source department no longer exists.");
                var target = await unitOfWork.Departments.GetByIdAsync(transfer.TargetDepartmentId, token) ?? throw Conflict("DEPARTMENT_UNAVAILABLE", "The target department no longer exists.");
                if (source.Status != EntityStatus.Active || target.Status != EntityStatus.Active) throw Conflict("DEPARTMENT_UNAVAILABLE", "A transfer department is locked.");
                await transaction.LockDevicesAsync(items.Select(x => x.DeviceId).ToArray(), token);
                var devices = await LoadTransferDevicesAsync(items, token);
                if (devices.Count != items.Count) throw Conflict("DEVICE_NOT_IN_SOURCE_DEPARTMENT", "A transfer device no longer exists.");
                foreach (var device in devices)
                {
                    ValidateTransferDevice(device, transfer.SourceDepartmentId);
                    var acquired = await unitOfWork.DeviceOperationHolds.TryAcquireAsync(new DeviceOperationHold
                    {
                        DeviceCode = device.DeviceCode, DeviceId = device.Id, OperationKind = DeviceOperationKind.Transfer,
                        OperationId = transfer.Id, HeldAt = clock.GetUtcNow()
                    }, token);
                    if (!acquired) throw Conflict("DEVICE_BUSY", "A transfer device is already held by another operation.");
                }
                transfer.Status = DeviceTransferStatus.Approved;
            }
            else transfer.Status = DeviceTransferStatus.Rejected;
            transfer.DecisionAccountId = actor.AccountId;
            transfer.DecidedAt = clock.GetUtcNow();
            transfer.DecisionReason = Clean(request.Reason);
            unitOfWork.DeviceTransfers.Update(transfer);
            await AuditAsync(transfer, actor.AccountId, approve ? "TRANSFER_APPROVED" : "TRANSFER_REJECTED",
                ["status", "decisionAccount", "decidedAt", "decisionReason"], before, SnapshotStatus(transfer), token);
            await unitOfWork.CommitAsync(token);
            return true;
        }, ct);

    public async Task HandoverCompleteAsync(string transferCode, TransferHandoverRequest request, CancellationToken ct)
        => await transaction.ExecuteAsync(async token =>
        {
            var actor = await RequireDepartmentStaffAsync(token);
            Validate(request);
            var transfer = await unitOfWork.DeviceTransfers.FirstOrDefaultTrackedAsync(x => x.TransferCode == NormalizeCode(transferCode), token) ?? throw Missing();
            if (transfer.Status != DeviceTransferStatus.Approved) throw Conflict("INVALID_WORKFLOW_TRANSITION", "Only an approved transfer can be completed.");
            if (actor.DepartmentId != transfer.SourceDepartmentId && actor.DepartmentId != transfer.TargetDepartmentId) throw Missing();
            var sourceHandover = await FindHandoverAccountAsync(request.SourceHandoverAccountCode, transfer.SourceDepartmentId, token);
            var targetHandover = await FindHandoverAccountAsync(request.TargetHandoverAccountCode, transfer.TargetDepartmentId, token);
            if (actor.AccountId != sourceHandover.Id && actor.AccountId != targetHandover.Id)
                throw Forbidden("The current actor must be one of the declared handover participants.");
            var items = await unitOfWork.DeviceTransferItems.ListTrackedForTransferAsync(transfer.Id, token);
            if (request.Items is null || request.Items.Count != items.Count || items.Count == 0)
                throw Validation("Handover must include every transfer device exactly once.");
            if (request.Items.Any(x => x is null || !Enum.IsDefined(x.Condition) || x.Condition == DeviceCondition.Missing || x.Note?.Length > 1000))
                throw Validation("Handover item condition or note is invalid.");
            var codes = request.Items.Select(x => NormalizeCode(x.DeviceCode)).ToArray();
            if (codes.Distinct(StringComparer.Ordinal).Count() != codes.Length || codes.Any(x => !DeviceCodePattern.IsMatch(x)))
                throw Validation("Handover contains duplicate or invalid device codes.");
            await transaction.LockDevicesAsync(items.Select(x => x.DeviceId), token);
            var devices = await LoadTransferDevicesAsync(items, token);
            var devicesById = devices.ToDictionary(x => x.Id);
            var actualCodes = items.Select(x => devicesById.TryGetValue(x.DeviceId, out var d) ? d.DeviceCode : "").ToHashSet(StringComparer.Ordinal);
            if (actualCodes.Contains("") || !actualCodes.SetEquals(codes)) throw Validation("Handover must include every transfer device exactly once.");
            var requestByCode = request.Items.ToDictionary(x => NormalizeCode(x.DeviceCode), StringComparer.Ordinal);
            var sourceCode = await DepartmentCodeAsync(transfer.SourceDepartmentId, token);
            var targetCode = await DepartmentCodeAsync(transfer.TargetDepartmentId, token);
            var responsibleCode = await AccountCodeAsync(transfer.TargetResponsibleAccountId, token);
            var before = Snapshot(transfer, sourceCode, targetCode, responsibleCode,
                items.Select(x => new { deviceCode = devicesById[x.DeviceId].DeviceCode, x.ConditionAtHandover, x.HandoverNote }));
            foreach (var item in items)
            {
                var device = devicesById[item.DeviceId];
                ValidateTransferDevice(device, transfer.SourceDepartmentId);
                if (!await unitOfWork.DeviceOperationHolds.IsOwnedByAsync(device.DeviceCode, DeviceOperationKind.Transfer, transfer.Id, token))
                    throw Conflict("DEVICE_BUSY", "The transfer no longer owns a device reservation.");
                var input = requestByCode[device.DeviceCode];
                item.ConditionAtHandover = input.Condition;
                item.HandoverNote = Clean(input.Note);
                device.DepartmentId = transfer.TargetDepartmentId;
                device.AreaId = null;
                device.ResponsibleAccountId = transfer.TargetResponsibleAccountId;
                device.Condition = input.Condition;
                unitOfWork.DeviceTransferItems.Update(item);
                unitOfWork.Devices.Update(device);
                await unitOfWork.DeviceHistories.AddAsync(new DeviceHistory
                {
                    RequestId = requestContext.RequestId, ActorAccountId = actor.AccountId, DeviceId = device.Id,
                    ActionCode = "DEVICE_TRANSFER_COMPLETED_" + device.DeviceCode, Outcome = AuditOutcome.Success,
                    OccurredAt = clock.GetUtcNow(), ChangedFields = "[\"departmentCode\",\"areaCode\",\"responsibleAccountCode\",\"condition\"]",
                    BeforeData = JsonSerializer.Serialize(new { device.DeviceCode, departmentCode = sourceCode }),
                    AfterData = JsonSerializer.Serialize(new { device.DeviceCode, departmentCode = targetCode, responsibleAccountCode = responsibleCode, condition = input.Condition.ToString(), transferCode = transfer.TransferCode })
                }, token);
                if (!await unitOfWork.DeviceOperationHolds.ReleaseAsync(device.DeviceCode, DeviceOperationKind.Transfer, transfer.Id, token))
                    throw Conflict("DEVICE_BUSY", "The transfer no longer owns a device reservation.");
            }
            transfer.SourceHandoverAccountId = sourceHandover.Id;
            transfer.TargetHandoverAccountId = targetHandover.Id;
            transfer.CompletedAt = clock.GetUtcNow();
            transfer.Status = DeviceTransferStatus.Completed;
            unitOfWork.DeviceTransfers.Update(transfer);
            await AuditAsync(transfer, actor.AccountId, "TRANSFER_COMPLETED", ["status", "sourceHandoverAccount", "targetHandoverAccount", "completedAt", "items"],
                before, Snapshot(transfer, sourceCode, targetCode, responsibleCode,
                    request.Items.Select(x => new { deviceCode = NormalizeCode(x.DeviceCode), condition = x.Condition.ToString(), note = Clean(x.Note) })), token);
            await unitOfWork.CommitAsync(token);
            return true;
        }, ct);

    public async Task CancelAsync(string transferCode, CancellationToken ct)
        => await transaction.ExecuteAsync(async token =>
        {
            var actor = await RequireDepartmentStaffAsync(token);
            var transfer = await unitOfWork.DeviceTransfers.FirstOrDefaultTrackedAsync(x => x.TransferCode == NormalizeCode(transferCode), token) ?? throw Missing();
            RequirePending(transfer);
            if (transfer.CreatorAccountId != actor.AccountId) throw Forbidden("Only the transfer creator may cancel it.");
            var before = SnapshotStatus(transfer);
            transfer.Status = DeviceTransferStatus.Cancelled;
            unitOfWork.DeviceTransfers.Update(transfer);
            await AuditAsync(transfer, actor.AccountId, "TRANSFER_CANCELLED", ["status"], before, SnapshotStatus(transfer), token);
            await unitOfWork.CommitAsync(token);
            return true;
        }, ct);

    public async Task<PagedResult<DeviceTransferHistoryDto>> HistoryAsync(string transferCode, DeviceTransferHistoryQuery query, CancellationToken ct)
    {
        var actor = await RequireActorAsync(ct);
        Validate(query);
        var transfer = await GetScopedAsync(transferCode, actor, ct);
        var page = await unitOfWork.DeviceTransferHistories.ListForTransferAsync(transfer.Id, query, ct);
        return new(page.Items.Select(x => new DeviceTransferHistoryDto(x.ActionCode, x.Outcome.ToString(), x.OccurredAt,
            x.ActorAccountCode, x.ActorFullName, x.ReasonCode,
            actor.RoleCode == RoleCodes.DepartmentStaff ? null : x.ChangedFields,
            actor.RoleCode == RoleCodes.DepartmentStaff ? null : x.BeforeData,
            actor.RoleCode == RoleCodes.DepartmentStaff ? null : x.AfterData)).ToArray(), page.TotalCount, page.Page, page.PageSize);
    }

    private async Task<(Department Source, Department Target, Account Responsible, string Reason, List<Device> Devices)> ValidateTransferRequestAsync(
        CreateDeviceTransferRequest request, Actor actor, IReadOnlyList<Device> existingDevices, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.SourceDepartmentCode) || string.IsNullOrWhiteSpace(request.TargetDepartmentCode)
            || string.IsNullOrWhiteSpace(request.TargetResponsibleAccountCode) || string.IsNullOrWhiteSpace(request.Reason))
            throw Validation("Source, target, target responsible account and reason are required.");
        if (request.Items is null || request.Items.Count is < 1 or > 200) throw Validation("A transfer must contain between 1 and 200 devices.");
        var source = await FindByCodeAsync(unitOfWork.Departments, request.SourceDepartmentCode, x => x.Code, "DEPARTMENT_NOT_FOUND", ct);
        var target = await FindByCodeAsync(unitOfWork.Departments, request.TargetDepartmentCode, x => x.Code, "DEPARTMENT_NOT_FOUND", ct);
        if (source.Id == target.Id) throw Validation("Source and target departments must be different.");
        if (source.Status != EntityStatus.Active || target.Status != EntityStatus.Active) throw Conflict("DEPARTMENT_UNAVAILABLE", "Both transfer departments must be active.");
        if (actor.DepartmentId != source.Id && actor.DepartmentId != target.Id) throw Forbidden("The creator must belong to the source or target department.");
        var responsible = await FindByCodeAsync(unitOfWork.Accounts, request.TargetResponsibleAccountCode, x => x.AccountCode, "TARGET_RESPONSIBLE_NOT_FOUND", ct);
        if (responsible.IsLock) throw Conflict("TARGET_RESPONSIBLE_UNAVAILABLE", "The target responsible account is locked.");
        var responsibleProfile = await unitOfWork.UserProfiles.GetByIdAsync(responsible.UserProfileId, ct);
        if (responsibleProfile?.DepartmentId != target.Id) throw Validation("Target responsible account must belong to the target department.");
        var reason = TextNormalization.Name(request.Reason);
        if (reason.Length == 0) throw Validation("Reason is required.");
        var existingByCode = existingDevices.ToDictionary(x => x.DeviceCode, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var devices = new List<Device>(request.Items.Count);
        foreach (var item in request.Items)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.DeviceCode)) throw Validation("Every transfer item requires a deviceCode.");
            var code = NormalizeCode(item.DeviceCode);
            if (!DeviceCodePattern.IsMatch(code)) throw Validation("Device code format is invalid.");
            if (!seen.Add(code)) throw Validation("A transfer cannot contain duplicate device codes.");
            var device = existingByCode.GetValueOrDefault(code)
                ?? await unitOfWork.Devices.FirstOrDefaultAsync(x => x.DeviceCode == code, ct)
                ?? throw new UseCaseException(FailureKind.NotFound, "DEVICE_NOT_FOUND", "A requested device was not found.");
            ValidateTransferDevice(device, source.Id);
            if (await unitOfWork.DeviceOperationHolds.ExistsForDeviceAsync(device.Id, ct)) throw Conflict("DEVICE_BUSY", "A transfer device is already held by another operation.");
            var subtree = await unitOfWork.Devices.GetSubtreeAsync(device.Id, ct);
            if (subtree.Any(x => x.DepartmentId != source.Id)) throw Conflict("DEVICE_GROUP_SOURCE_MISMATCH", "Every device in the selected subtree must belong to the transfer source department.");
            foreach (var member in subtree)
            {
                if (devices.Any(x => x.Id == member.Id)) continue;
                ValidateTransferDevice(member, source.Id);
                if (await unitOfWork.DeviceOperationHolds.ExistsForDeviceAsync(member.Id, ct)) throw Conflict("DEVICE_BUSY", "A device in the selected subtree is already held by another operation.");
                devices.Add(member);
            }
        }
        return (source, target, responsible, reason, devices);
    }

    private async Task<List<Device>> LoadTransferDevicesAsync(IEnumerable<DeviceTransferItem> items, CancellationToken ct)
    {
        var result = new List<Device>();
        foreach (var item in items)
        {
            var device = await unitOfWork.Devices.FirstOrDefaultTrackedAsync(x => x.Id == item.DeviceId, ct);
            if (device is not null) result.Add(device);
        }
        return result;
    }

    private async Task<Account> GetTargetResponsibleAsync(Guid accountId, Guid targetDepartmentId, CancellationToken ct)
    {
        var account = await unitOfWork.Accounts.GetByIdAsync(accountId, ct) ?? throw Conflict("TARGET_RESPONSIBLE_UNAVAILABLE", "The target responsible account no longer exists.");
        var profile = await unitOfWork.UserProfiles.GetByIdAsync(account.UserProfileId, ct);
        if (profile?.DepartmentId != targetDepartmentId) throw Conflict("TARGET_RESPONSIBLE_UNAVAILABLE", "The target responsible account no longer belongs to the target department.");
        return account;
    }

    private async Task<Account> FindHandoverAccountAsync(string accountCode, Guid departmentId, CancellationToken ct)
    {
        var account = await FindByCodeAsync(unitOfWork.Accounts, accountCode, x => x.AccountCode, "HANDOVER_ACCOUNT_NOT_FOUND", ct);
        var profile = await unitOfWork.UserProfiles.GetByIdAsync(account.UserProfileId, ct);
        var role = await unitOfWork.Roles.GetByIdAsync(account.RoleId, ct);
        if (account.IsLock || profile?.DepartmentId != departmentId || role?.Code != RoleCodes.DepartmentStaff)
            throw Validation("Handover accounts must be active Department Staff members of their respective departments.");
        return account;
    }

    private static void ValidateTransferDevice(Device device, Guid sourceDepartmentId)
    {
        if (device.DepartmentId != sourceDepartmentId) throw Conflict("DEVICE_NOT_IN_SOURCE_DEPARTMENT", "Every device must still belong to the source department.");
        if (device.Status != EntityStatus.Active) throw Conflict("DEVICE_LOCKED", "A locked device cannot be transferred.");
        if (device.Condition != DeviceCondition.Good) throw Conflict("DEVICE_NOT_AVAILABLE", "A damaged or missing device cannot be transferred.");
    }

    private async Task<Actor> RequireActorAsync(CancellationToken ct)
    {
        if (current.AccountId is not { } id || current.SessionId is not { } sessionId) throw Unauthorized();
        var account = await unitOfWork.Accounts.FirstOrDefaultAsync(x => x.Id == id && !x.IsLock, ct);
        var session = await unitOfWork.AuthSessions.FirstOrDefaultAsync(x => x.Id == sessionId && x.AccountId == id && x.IsActive && x.ExpiresAt > clock.GetUtcNow(), ct);
        if (account is null || session is null) throw Unauthorized();
        var role = await unitOfWork.Roles.GetByIdAsync(account.RoleId, ct);
        if (role?.Code is not (RoleCodes.EquipmentManager or RoleCodes.EquipmentStaff or RoleCodes.DepartmentStaff)) throw Forbidden("Transfer access is not permitted.");
        var profile = await unitOfWork.UserProfiles.GetByIdAsync(account.UserProfileId, ct) ?? throw Unauthorized();
        return new(id, role.Code, profile.DepartmentId);
    }

    private async Task<Actor> RequireDepartmentStaffAsync(CancellationToken ct)
    {
        var actor = await RequireActorAsync(ct);
        if (actor.RoleCode != RoleCodes.DepartmentStaff) throw Forbidden("Only department staff may perform this action.");
        return actor;
    }

    private async Task<Actor> RequireEquipmentAsync(CancellationToken ct)
    {
        var actor = await RequireActorAsync(ct);
        if (actor.RoleCode is not (RoleCodes.EquipmentManager or RoleCodes.EquipmentStaff)) throw Forbidden("Only QTTB may decide transfer requests.");
        return actor;
    }

    private async Task<DeviceTransferReadModel> GetScopedAsync(string code, Actor actor, CancellationToken ct)
        => await unitOfWork.DeviceTransfers.GetByCodeAsync(NormalizeCode(code), actor.RoleCode == RoleCodes.DepartmentStaff ? actor.DepartmentId : null,
            actor.AccountId, ct) ?? throw Missing();

    private async Task<DeviceTransferDto> ToDetailAsync(DeviceTransferReadModel read, CancellationToken ct)
    {
        var items = await unitOfWork.DeviceTransferItems.ListForTransferAsync(read.Id, ct);
        return ToDto(read) with { Items = items.Select(x => new DeviceTransferItemDto(x.DeviceCode, x.Name, x.CategoryCode, x.CategoryName,
            x.ConditionAtHandover, x.HandoverNote)).ToArray() };
    }

    private static DeviceTransferDto ToDto(DeviceTransferReadModel x)
        => new(x.TransferCode, x.SourceDepartmentCode, x.SourceDepartmentName, x.TargetDepartmentCode, x.TargetDepartmentName,
            x.TargetResponsibleAccountCode, x.TargetResponsibleFullName, x.Reason, x.Status, x.CreatorAccountCode, x.CreatedAt,
            x.DecisionAccountCode, x.DecidedAt, x.DecisionReason, x.SourceHandoverAccountCode, x.TargetHandoverAccountCode,
            x.CompletedAt, x.Notes, x.ItemCount, x.Version);

    private Task AuditAsync(DeviceTransfer transfer, Guid actor, string action, IReadOnlyList<string> fields, string? before, string after, CancellationToken ct)
        => unitOfWork.DeviceTransferHistories.AddAsync(new DeviceTransferHistory
        {
            RequestId = requestContext.RequestId, ActorAccountId = actor, TransferId = transfer.Id, ActionCode = action,
            Outcome = AuditOutcome.Success, OccurredAt = clock.GetUtcNow(), ChangedFields = JsonSerializer.Serialize(fields),
            BeforeData = before, AfterData = after
        }, ct);

    private async Task<T> FindByCodeAsync<T>(IRepository<T> repository, string code, System.Linq.Expressions.Expression<Func<T, string>> selector, string errorCode, CancellationToken ct) where T : class
    {
        if (string.IsNullOrWhiteSpace(code)) throw Validation("A required code is missing.");
        var value = code.Trim().ToLowerInvariant();
        var body = System.Linq.Expressions.Expression.Equal(System.Linq.Expressions.Expression.Call(selector.Body, typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!), System.Linq.Expressions.Expression.Constant(value));
        return await repository.FirstOrDefaultAsync(System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(body, selector.Parameters[0]), ct)
            ?? throw new UseCaseException(FailureKind.NotFound, errorCode, "The selected related record was not found.");
    }

    private async Task<string> DepartmentCodeAsync(Guid id, CancellationToken ct)
        => (await unitOfWork.Departments.GetByIdAsync(id, ct))?.Code ?? "";
    private async Task<string> AccountCodeAsync(Guid id, CancellationToken ct)
        => (await unitOfWork.Accounts.GetByIdAsync(id, ct))?.AccountCode ?? "";
    private static string Snapshot(DeviceTransfer transfer, string sourceCode, string targetCode, string responsibleCode, IEnumerable<object> items)
        => JsonSerializer.Serialize(new { transfer.TransferCode, sourceCode, targetCode, responsibleCode, transfer.Reason, transfer.Notes, items });
    private static string SnapshotStatus(DeviceTransfer transfer)
        => JsonSerializer.Serialize(new { transfer.TransferCode, status = transfer.Status.ToString(), transfer.DecisionReason, transfer.DecidedAt, transfer.CompletedAt });
    private static void RequirePending(DeviceTransfer transfer)
    {
        if (transfer.Status != DeviceTransferStatus.PendingApproval) throw Conflict("INVALID_WORKFLOW_TRANSITION", "Only a pending transfer can be changed or decided.");
    }
    private static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : TextNormalization.Name(value);
    private static void Validate(object request)
    {
        if (!Validator.TryValidateObject(request, new ValidationContext(request), [], true)) throw Validation("Request validation failed.");
    }
    private static UseCaseException Unauthorized() => new(FailureKind.Unauthorized, "AUTHENTICATION_REQUIRED", "A valid session is required.");
    private static UseCaseException Forbidden(string message) => new(FailureKind.Forbidden, "WORKFLOW_ACTION_FORBIDDEN", message);
    private static UseCaseException Missing() => new(FailureKind.NotFound, "TRANSFER_NOT_FOUND", "Transfer was not found.");
    private static UseCaseException Validation(string message) => new(FailureKind.Validation, "VALIDATION_FAILED", message);
    private static UseCaseException Conflict(string code, string message) => new(FailureKind.Conflict, code, message);
    private sealed record Actor(Guid AccountId, string RoleCode, Guid DepartmentId);
}
