using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CSVC.Core.Accounts;
using CSVC.Core.Common;
using CSVC.Core.DeviceTransfers;
using CSVC.Core.Devices;
using CSVC.UseCases.DeviceTransfers.DTOs;

namespace CSVC.UseCases.DeviceTransfers;

public sealed partial class DeviceTransferService
{
    public async Task<DeviceTransferDto> CreateRecoveryOrderAsync(CreateRecoveryOrderRequest request, CancellationToken ct)
        => await transaction.ExecuteAsync(async token =>
        {
            var actor = await RequireEquipmentAsync(token);
            Validate(request);
            if (request.RequestId == Guid.Empty) throw Validation("A nonempty requestId is required.");
            if (request.Items.Any(i => i is null || string.IsNullOrWhiteSpace(i.DeviceCode))) throw Validation("Every item needs a device code.");
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                source = NormalizeCode(request.SourceDepartmentCode), responsible = NormalizeCode(request.TargetResponsibleAccountCode),
                reason = TextNormalization.Name(request.Reason), notes = Clean(request.Notes),
                items = request.Items.Select(i => NormalizeCode(i.DeviceCode)).Order().ToArray()
            }))));
            var prior = await unitOfWork.DeviceTransfers.FirstOrDefaultAsync(t => t.CreateRequestId == request.RequestId && t.CreatorAccountId == actor.AccountId, token);
            if (prior is not null)
            {
                if (prior.CreateRequestHash != hash) throw Conflict("IDEMPOTENCY_CONFLICT", "The requestId was already used with different input.");
                return await GetAsync(prior.TransferCode, token);
            }
            var prepared = await RecoveryBodyAsync(request, token);
            var (source, target, responsible, reason, devices, _) = await ValidateTransferRequestAsync(prepared, actor, [], token, true);
            await transaction.LockDevicesAsync(devices.Select(d => d.Id), token);
            var transfer = new DeviceTransfer
            {
                TransferCode = BusinessCode.NewRandom("DC"), SourceDepartmentId = source.Id, TargetDepartmentId = target.Id,
                TargetResponsibleAccountId = responsible.Id, Reason = reason, Notes = Clean(request.Notes),
                CreatorAccountId = actor.AccountId, CreatedAt = clock.GetUtcNow(), TransferType = DeviceTransferType.Recovery,
                InitiationType = TransferInitiationType.EquipmentDepartment, Status = DeviceTransferStatus.PendingConfirmation,
                CreateRequestId = request.RequestId, CreateRequestHash = hash,
                Items = devices.Select(d => new DeviceTransferItem { DeviceId = d.Id }).ToList()
            };
            await unitOfWork.DeviceTransfers.AddAsync(transfer, token);
            await AuditAsync(transfer, actor.AccountId, "RECOVERY_ORDER_CREATED", ["source", "target", "items", "initiationType"], null,
                Snapshot(transfer, source.Code, target.Code, responsible.AccountCode, devices.Select(d => new { d.DeviceCode, d.Name })), token);
            await unitOfWork.CommitAsync(token);
            return await GetAsync(transfer.TransferCode, token);
        }, ct);

    public async Task UpdateRecoveryOrderAsync(string code, UpdateRecoveryOrderRequest request, CancellationToken ct)
        => await transaction.ExecuteAsync(async token =>
        {
            var actor = await RequireEquipmentAsync(token);
            Validate(request);
            var transfer = await LoadRecoveryOrderAsync(code, actor, request.Version, token);
            await RequireEquipmentCreatorAsync(transfer, actor, token);
            RequireConfirmationPending(transfer);
            var prepared = await RecoveryBodyAsync(request, token);
            var oldItems = await unitOfWork.DeviceTransferItems.ListTrackedForTransferAsync(transfer.Id, token);
            var oldDevices = await LoadTransferDevicesAsync(oldItems, token);
            var (source, target, responsible, reason, devices, _) = await ValidateTransferRequestAsync(prepared, actor, oldDevices, token, true);
            if (source.Id != transfer.SourceDepartmentId) throw Validation("The source department is fixed after issue; cancel and create a new order to change it.");
            await transaction.LockDevicesAsync(oldDevices.Concat(devices).Select(d => d.Id), token);
            var before = Snapshot(transfer, source.Code, target.Code, await AccountCodeAsync(transfer.TargetResponsibleAccountId, token), oldDevices.Select(d => new { d.DeviceCode, d.Name }));
            var retained = devices.Select(d => d.Id).ToHashSet();
            foreach (var item in oldItems.Where(i => !retained.Contains(i.DeviceId))) await unitOfWork.DeviceTransferItems.DeleteAsync(item, token);
            var existing = oldItems.Select(i => i.DeviceId).ToHashSet();
            foreach (var device in devices.Where(d => !existing.Contains(d.Id))) await unitOfWork.DeviceTransferItems.AddAsync(new DeviceTransferItem { TransferId = transfer.Id, DeviceId = device.Id }, token);
            transfer.TargetResponsibleAccountId = responsible.Id; transfer.Reason = reason; transfer.Notes = Clean(request.Notes);
            unitOfWork.DeviceTransfers.Update(transfer);
            await AuditAsync(transfer, actor.AccountId, "RECOVERY_ORDER_UPDATED", ["reason", "items", "responsible", "notes"], before,
                Snapshot(transfer, source.Code, target.Code, responsible.AccountCode, devices.Select(d => new { d.DeviceCode, d.Name })), token);
            return true;
        }, ct);

    public async Task DecideRecoveryOrderAsync(string code, bool accept, TransferDecisionRequest request, CancellationToken ct)
        => await transaction.ExecuteAsync(async token =>
        {
            var actor = await RequireDepartmentStaffAsync(token);
            Validate(request);
            var transfer = await LoadRecoveryOrderAsync(code, actor, request.Version, token);
            if (actor.DepartmentId != transfer.SourceDepartmentId) throw Forbidden("Only source staff may accept or reject this recovery.");
            RequireConfirmationPending(transfer);
            await RequireEquipmentTargetAsync(transfer, token);
            var source = await unitOfWork.Departments.GetByIdAsync(transfer.SourceDepartmentId, token);
            if (source?.Status != EntityStatus.Active) throw Conflict("DEPARTMENT_UNAVAILABLE", "The source department is unavailable.");
            if (!accept && Clean(request.Reason) is null) throw Validation("A rejection reason is required.");
            var before = SnapshotStatus(transfer);
            if (accept)
            {
                var items = await unitOfWork.DeviceTransferItems.ListTrackedForTransferAsync(transfer.Id, token);
                await ReserveAsync(transfer, items, token);
                transfer.Status = DeviceTransferStatus.Approved;
                transfer.SourceAcceptedByAccountId = actor.AccountId; transfer.SourceAcceptedAt = clock.GetUtcNow();
            }
            else
            {
                transfer.Status = DeviceTransferStatus.Rejected;
                transfer.SourceRejectedByAccountId = actor.AccountId; transfer.SourceRejectedAt = clock.GetUtcNow();
                transfer.SourceRejectionReason = Clean(request.Reason);
            }
            unitOfWork.DeviceTransfers.Update(transfer);
            await AuditAsync(transfer, actor.AccountId, accept ? "RECOVERY_SOURCE_ACCEPTED" : "RECOVERY_SOURCE_REJECTED",
                ["status", "sourceDecision"], before, SnapshotStatus(transfer), token);
            return true;
        }, ct);

    public async Task ConfirmSourceHandoverAsync(string code, TransferHandoverRequest request, CancellationToken ct)
        => await transaction.ExecuteAsync(async token =>
        {
            var actor = await RequireDepartmentStaffAsync(token);
            Validate(request);
            var transfer = await LoadRecoveryOrderAsync(code, actor, request.Version, token);
            if (actor.DepartmentId != transfer.SourceDepartmentId) throw Forbidden("Only source staff may confirm their physical handover.");
            if (transfer.Status != DeviceTransferStatus.Approved || transfer.SourceAcceptedAt is null || transfer.SourceHandoverConfirmedAt is not null)
                throw Conflict("INVALID_WORKFLOW_TRANSITION", "The source has not accepted or has already handed over this recovery.");
            if (request.SourceHandoverAccountCode is not null) throw Validation("The handover actor comes from the authenticated session.");
            await RequireEquipmentTargetAsync(transfer, token);
            var responsible = await GetTargetResponsibleAsync(transfer.TargetResponsibleAccountId, transfer.TargetDepartmentId, token);
            if (responsible.IsLock) throw Conflict("TARGET_RESPONSIBLE_UNAVAILABLE", "The target responsible account is locked.");
            var items = await unitOfWork.DeviceTransferItems.ListTrackedForTransferAsync(transfer.Id, token);
            if (request.Items is null || request.Items.Count != items.Count || items.Count == 0 || request.Items.Any(i => i is null
                || i.Condition is not (DeviceCondition.Good or DeviceCondition.Damaged) || i.Note?.Length > 1000 || string.IsNullOrWhiteSpace(i.DeviceCode)))
                throw Validation("Supply every recovery device once with a Good or Damaged condition.");
            var inputs = request.Items.Select(i => NormalizeCode(i.DeviceCode)).ToArray();
            if (inputs.Distinct().Count() != inputs.Length) throw Validation("Duplicate device codes.");
            await transaction.LockDevicesAsync(items.Select(i => i.DeviceId), token);
            var devices = await LoadTransferDevicesAsync(items, token);
            if (devices.Count != items.Count || !devices.Select(d => d.DeviceCode).ToHashSet().SetEquals(inputs)) throw Validation("Include every device in this order exactly once.");
            var source = await unitOfWork.Departments.GetByIdAsync(transfer.SourceDepartmentId, token);
            if (source?.Status != EntityStatus.Active) throw Conflict("DEPARTMENT_UNAVAILABLE", "The source department is unavailable.");
            var before = SnapshotStatus(transfer);
            foreach (var item in items)
            {
                var device = devices.Single(d => d.Id == item.DeviceId);
                ValidateTransferDevice(device, transfer.SourceDepartmentId, DeviceTransferType.Recovery);
                await reservations.RequireAsync(device, true, DeviceOperationKind.Transfer, transfer.Id, token);
                var input = request.Items.Single(i => NormalizeCode(i.DeviceCode) == device.DeviceCode);
                item.ConditionAtSourceHandover = input.Condition; item.SourceHandoverNote = Clean(input.Note);
                unitOfWork.DeviceTransferItems.Update(item);
            }
            transfer.SourceHandoverConfirmedByAccountId = actor.AccountId; transfer.SourceHandoverConfirmedAt = clock.GetUtcNow();
            unitOfWork.DeviceTransfers.Update(transfer);
            await AuditAsync(transfer, actor.AccountId, "RECOVERY_SOURCE_HANDED_OVER", ["sourceHandover", "itemConditions"], before,
                JsonSerializer.Serialize(new { state = SnapshotStatus(transfer), items = request.Items.Select(i => new { i.DeviceCode, i.Condition, i.Note }) }), token);
            return true;
        }, ct);

    private async Task<CreateDeviceTransferRequest> RecoveryBodyAsync(RecoveryOrderBody request, CancellationToken ct)
    {
        var id = await equipmentPolicy.RequireConfiguredAsync(ct);
        return new CreateDeviceTransferRequest { SourceDepartmentCode = request.SourceDepartmentCode,
            TargetDepartmentCode = await DepartmentCodeAsync(id, ct), TargetResponsibleAccountCode = request.TargetResponsibleAccountCode,
            Reason = request.Reason, Items = request.Items, Notes = request.Notes };
    }

    private async Task<DeviceTransfer> LoadForActorAsync(string code, Actor actor, CancellationToken ct)
    {
        var transfer = await unitOfWork.DeviceTransfers.FirstOrDefaultTrackedAsync(t => t.TransferCode == NormalizeCode(code), ct) ?? throw Missing();
        if (actor.RoleCode == RoleCodes.DepartmentStaff && actor.DepartmentId != transfer.SourceDepartmentId && actor.DepartmentId != transfer.TargetDepartmentId) throw Missing();
        return transfer;
    }
    private async Task<DeviceTransfer> LoadRecoveryOrderAsync(string code, Actor actor, int? version, CancellationToken ct)
    {
        var transfer = await LoadForActorAsync(code, actor, ct);
        if (transfer.InitiationType != TransferInitiationType.EquipmentDepartment || transfer.TransferType != DeviceTransferType.Recovery)
            throw Conflict("INVALID_WORKFLOW_TRANSITION", "This action requires an equipment-initiated recovery order.");
        RequireVersion(transfer, version);
        return transfer;
    }
    private static void RequireVersion(DeviceTransfer transfer, int? version)
    {
        if (version is null) throw Validation("The current version is required.");
        if (transfer.Version != version) throw Conflict("VERSION_CONFLICT", "Reload the transfer before continuing.");
    }
    private static void RequireSourceInitiation(DeviceTransfer transfer)
    {
        if (transfer.InitiationType != TransferInitiationType.SourceDepartment)
            throw Conflict("INVALID_WORKFLOW_TRANSITION", "Use the recovery order action for this initiation type.");
    }
    private async Task RequireEquipmentTargetAsync(DeviceTransfer transfer, CancellationToken ct)
    {
        if (transfer.TargetDepartmentId != await equipmentPolicy.RequireConfiguredAsync(ct))
            throw Conflict("EQUIPMENT_DEPARTMENT_CHANGED", "The configured equipment department no longer matches this recovery.");
    }
    private async Task RequireEquipmentCreatorAsync(DeviceTransfer transfer, Actor actor, CancellationToken ct)
    {
        if (actor.RoleCode is not (RoleCodes.EquipmentManager or RoleCodes.EquipmentStaff) || actor.AccountId != transfer.CreatorAccountId
            || actor.DepartmentId != transfer.TargetDepartmentId) throw Forbidden("Only the equipment creator may change this order.");
        await equipmentPolicy.RequireMemberAsync(actor.RoleCode, actor.DepartmentId, ct);
        await RequireEquipmentTargetAsync(transfer, ct);
    }
}
