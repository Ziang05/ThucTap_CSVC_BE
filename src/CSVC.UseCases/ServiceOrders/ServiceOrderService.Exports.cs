using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CSVC.Core.Accounts;
using CSVC.Core.Common;
using CSVC.Core.Devices;
using CSVC.Core.ServiceOrders;
using CSVC.UseCases.Common;
using CSVC.UseCases.ServiceOrders.DTOs;

namespace CSVC.UseCases.ServiceOrders;

public sealed partial class ServiceOrderService
{
    public async Task<PagedResult<ExportSlipDto>> ListExportsAsync(ExportSlipListQuery query, CancellationToken ct)
    {
        await RequireActorAsync(null, ct); Validate(query);
        var page = await orders.ListExportsAsync(query, ct);
        var parents = await orders.GetByExportIdsAsync(page.Items.Select(e => e.Id).ToArray(), ct);
        var codes = await AccountCodesAsync(parents, ct);
        var result = page.Items.Select(slip =>
        {
            var order = parents.Single(o => o.Id == slip.ServiceOrderId);
            return ToExportDto(order.ExportSlips.Single(e => e.Id == slip.Id), order, codes);
        }).ToArray();
        return new(result, page.TotalCount, page.Page, page.PageSize);
    }

    public async Task<ExportSlipDto> GetExportAsync(string code, CancellationToken ct)
    {
        await RequireActorAsync(null, ct);
        var order = await LoadExportAsync(code, false, ct);
        return ToExportDto(order.ExportSlips.Single(e => e.DisplayCode == Code(code)), order, await AccountCodesAsync([order], ct));
    }

    public Task<ExportSlipDto> CreateExportAsync(CreateExportSlipRequest request, CancellationToken ct)
        => transaction.ExecuteAsync(async token =>
        {
            var actor = await RequireActorAsync(null, token);
            Validate(request); RequireRequest(request.RequestId);
            ValidateSelection(request.ItemNos); RequireFacility(request.FacilityName);
            var hash = Hash(new { source = Code(request.ServiceOrderCode), facility = Clean(request.FacilityName),
                address = Clean(request.FacilityAddress), contact = Clean(request.FacilityContact),
                planned = request.PlannedExportAt?.ToUniversalTime(), notes = Clean(request.Notes), items = request.ItemNos.Order() });
            var prior = await exportHistory.FirstOrDefaultAsync(h => h.RequestId == request.RequestId && h.ActionCode == "CREATE", token);
            if (prior is not null)
            {
                if (prior.ActorAccountId != actor || prior.RequestPayloadHash != hash) throw IdempotencyConflict();
                var priorOrder = await LoadAsync(Code(request.ServiceOrderCode), false, token);
                return ToExportDto(priorOrder.ExportSlips.Single(e => e.Id == prior.ExportSlipId), priorOrder, await AccountCodesAsync([priorOrder], token));
            }
            var order = await LoadAsync(request.ServiceOrderCode, true, token);
            RequireStatus(order.Status, ServiceWorkflowStatus.InProgress);
            var selected = SelectItems(order, request.ItemNos);
            await transaction.LockDevicesAsync(selected.Select(i => i.DeviceId), token);
            foreach (var item in selected) await ValidateExportItemAsync(order, item, null, token);
            var now = clock.GetUtcNow();
            var slip = new ExportSlip { ServiceOrderId = order.Id, DisplayCode = BusinessCode.NewRandom("PX"),
                SequenceNo = order.ExportSlips.Select(e => e.SequenceNo).DefaultIfEmpty(0).Max() + 1,
                CreateRequestId = request.RequestId, CreateRequestHash = hash, CreatedByAccountId = actor, CreatedAt = now,
                FacilityNameSnapshot = Clean(request.FacilityName)!, FacilityAddressSnapshot = Clean(request.FacilityAddress),
                FacilityContactSnapshot = Clean(request.FacilityContact), PlannedExportAt = request.PlannedExportAt?.ToUniversalTime(), Notes = Clean(request.Notes) };
            foreach (var item in selected) await AddExportLineAsync(slip, item, token);
            order.ExportSlips.Add(slip);
            await AuditExportAsync(slip, request.RequestId, hash, actor, "CREATE", null, null, token);
            return ToExportDto(slip, order, await AccountCodesAsync([order], token));
        }, ct);

    public Task ApproveExportAsync(string code, ServiceTransitionRequest request, CancellationToken ct)
        => transaction.ExecuteAsync(async token =>
        {
            var actor = await RequireActorAsync(RoleCodes.EquipmentManager, token);
            Validate(request); RequireRequest(request.RequestId);
            var order = await LoadExportAsync(code, true, token); var slip = Export(order, code);
            var hash = Hash(new { request.Version });
            if (await ExportRetryAsync(slip, request.RequestId, hash, actor, "APPROVE", token)) return true;
            RequireVersion(slip.Version, request.Version); RequireStatus(slip.Status, ServiceWorkflowStatus.PendingApproval);
            RequireStatus(order.Status, ServiceWorkflowStatus.InProgress);
            var items = SelectItems(order, slip.Items.Select(l => order.Items.Single(i => i.Id == l.ServiceOrderItemId).ItemNo));
            await transaction.LockDevicesAsync(items.Select(i => i.DeviceId), token);
            foreach (var item in items) await ValidateExportItemAsync(order, item, slip.Items.Single(l => l.ServiceOrderItemId == item.Id).Id, token);
            var before = ExportSnapshot(slip);
            slip.Status = ServiceWorkflowStatus.InProgress; slip.ApprovedAt = clock.GetUtcNow();
            slip.ApprovedByAccountId = actor; slip.ApprovalRequestId = request.RequestId; slip.Version++;
            await AuditExportAsync(slip, request.RequestId, hash, actor, "APPROVE", before, null, token);
            return true;
        }, ct);

    public Task EditExportAsync(string code, EditExportSlipRequest request, CancellationToken ct)
        => transaction.ExecuteAsync(async token =>
        {
            var actor = await RequireActorAsync(null, token);
            Validate(request); RequireRequest(request.RequestId); ValidateSelection(request.ItemNos); RequireFacility(request.FacilityName);
            var order = await LoadExportAsync(code, true, token); var slip = Export(order, code);
            await RequireExportEditorAsync(slip, actor, token);
            var hash = Hash(new { request.Version, facility = Clean(request.FacilityName), address = Clean(request.FacilityAddress),
                contact = Clean(request.FacilityContact), planned = request.PlannedExportAt?.ToUniversalTime(), notes = Clean(request.Notes), items = request.ItemNos.Order() });
            if (await ExportRetryAsync(slip, request.RequestId, hash, actor, "EDIT", token)) return true;
            RequireVersion(slip.Version, request.Version); RequireStatus(slip.Status, ServiceWorkflowStatus.PendingApproval);
            RequireStatus(order.Status, ServiceWorkflowStatus.InProgress);
            var selected = SelectItems(order, request.ItemNos);
            var oldItems = order.Items.Where(i => slip.Items.Any(l => l.ServiceOrderItemId == i.Id)).ToArray();
            await transaction.LockDevicesAsync(selected.Concat(oldItems).Select(i => i.DeviceId), token);
            foreach (var item in selected)
                await ValidateExportItemAsync(order, item, slip.Items.SingleOrDefault(l => l.ServiceOrderItemId == item.Id)?.Id, token);
            var before = ExportSnapshot(slip);
            var changed = slip.FacilityNameSnapshot != Clean(request.FacilityName) || slip.FacilityAddressSnapshot != Clean(request.FacilityAddress)
                || slip.FacilityContactSnapshot != Clean(request.FacilityContact) || slip.PlannedExportAt != request.PlannedExportAt?.ToUniversalTime()
                || slip.Notes != Clean(request.Notes) || !oldItems.Select(i => i.ItemNo).Order().SequenceEqual(request.ItemNos.Order());
            if (changed)
            {
                foreach (var line in slip.Items.Where(l => !selected.Any(i => i.Id == l.ServiceOrderItemId)).ToArray())
                {
                    if (await exportHistory.ExistsAsync(h => h.ExportSlipItemId == line.Id, token)) throw InvalidTransition();
                    await RemoveGuardAsync(order.Items.Single(i => i.Id == line.ServiceOrderItemId), line, token);
                    slip.Items.Remove(line); await exportItems.DeleteAsync(line, token);
                }
                foreach (var item in selected.Where(i => !slip.Items.Any(l => l.ServiceOrderItemId == i.Id))) await AddExportLineAsync(slip, item, token);
                slip.FacilityNameSnapshot = Clean(request.FacilityName)!; slip.FacilityAddressSnapshot = Clean(request.FacilityAddress);
                slip.FacilityContactSnapshot = Clean(request.FacilityContact); slip.PlannedExportAt = request.PlannedExportAt?.ToUniversalTime();
                slip.Notes = Clean(request.Notes); slip.LastEditedAt = clock.GetUtcNow(); slip.LastEditedByAccountId = actor;
                slip.EditRequestId = request.RequestId; slip.Version++;
            }
            await AuditExportAsync(slip, request.RequestId, hash, actor, "EDIT", before, null, token, changed ? AuditOutcome.Success : AuditOutcome.NoChange);
            return true;
        }, ct);

    public Task CancelExportAsync(string code, CancelServiceOrderRequest request, CancellationToken ct)
        => transaction.ExecuteAsync(async token =>
        {
            var actor = await RequireActorAsync(null, token); Validate(request); RequireRequest(request.RequestId);
            var reason = Clean(request.Reason) ?? throw Validation("A cancel reason is required.");
            var order = await LoadExportAsync(code, true, token); var slip = Export(order, code);
            await RequireExportEditorAsync(slip, actor, token);
            var hash = Hash(new { request.Version, reason });
            if (await ExportRetryAsync(slip, request.RequestId, hash, actor, "CANCEL", token)) return true;
            RequireVersion(slip.Version, request.Version); RequireStatus(slip.Status, ServiceWorkflowStatus.PendingApproval);
            RequireStatus(order.Status, ServiceWorkflowStatus.InProgress);
            await transaction.LockDevicesAsync(order.Items.Where(i => slip.Items.Any(l => l.ServiceOrderItemId == i.Id)).Select(i => i.DeviceId), token);
            var before = ExportSnapshot(slip);
            foreach (var line in slip.Items) await RemoveGuardAsync(order.Items.Single(i => i.Id == line.ServiceOrderItemId), line, token);
            slip.Status = ServiceWorkflowStatus.Canceled; slip.CanceledAt = clock.GetUtcNow(); slip.CanceledByAccountId = actor;
            slip.CancelRequestId = request.RequestId; slip.CancelReason = reason; slip.Version++;
            await AuditExportAsync(slip, request.RequestId, hash, actor, "CANCEL", before, null, token);
            return true;
        }, ct);

    public Task RecordExportResultAsync(string code, RecordExportResultRequest request, CancellationToken ct)
        => transaction.ExecuteAsync(async token =>
        {
            var actor = await RequireActorAsync(null, token); Validate(request); RequireRequest(request.RequestId);
            if (request.Items is null || request.Items.Any(i => i is null)) throw Validation("Results are required.");
            ValidateSelection(request.Items.Select(i => i.ItemNo));
            foreach (var input in request.Items)
            {
                Validate(input);
                if (input.Result == ExportResult.Success && (Clean(input.RecipientName) is null || Clean(input.FailureReason) is not null))
                    throw Validation("Successful export requires a recipient and no failure reason.");
                if (input.Result == ExportResult.Failed && (Clean(input.FailureReason) is null || Clean(input.RecipientName) is not null || Clean(input.RecipientContact) is not null))
                    throw Validation("Failed export requires a reason and no recipient.");
                if (Clean(input.EvidenceRef) is { } evidence && Uri.TryCreate(evidence, UriKind.Absolute, out _))
                    throw Validation("Use an opaque evidence reference.");
            }
            var hash = Hash(new { request.Version, items = request.Items.OrderBy(i => i.ItemNo).Select(i => new { i.ItemNo, i.Result,
                recipient = Clean(i.RecipientName), contact = Clean(i.RecipientContact), reason = Clean(i.FailureReason), evidence = Clean(i.EvidenceRef) }) });
            var order = await LoadExportAsync(code, true, token); var slip = Export(order, code);
            if (await ExportRetryAsync(slip, request.RequestId, hash, actor, "ITEM_RESULT_RECORDED", token)) return true;
            RequireVersion(slip.Version, request.Version); RequireStatus(slip.Status, ServiceWorkflowStatus.InProgress);
            var selected = SelectItems(order, request.Items.Select(i => i.ItemNo));
            if (selected.Any(i => !slip.Items.Any(l => l.ServiceOrderItemId == i.Id))) throw Validation("Item is not in this export.");
            await transaction.LockDevicesAsync(selected.Select(i => i.DeviceId), token);
            foreach (var item in selected)
            {
                var line = slip.Items.Single(l => l.ServiceOrderItemId == item.Id); var input = request.Items.Single(i => i.ItemNo == item.ItemNo);
                if (line.Result is not null) throw InvalidTransition();
                var guard = await OwnGuardAsync(item, line, ExportGuardState.HeldForExport, token);
                if (input.Result == ExportResult.Success) await ValidateExportItemAsync(order, item, line.Id, token);
                line.Result = input.Result; line.ResultRecordedByAccountId = actor; line.ResultRecordedAt = clock.GetUtcNow();
                line.ResultRequestId = request.RequestId; line.RecipientName = Clean(input.RecipientName); line.RecipientContact = Clean(input.RecipientContact);
                line.FailureReason = Clean(input.FailureReason); line.EvidenceRef = Clean(input.EvidenceRef); line.Version++;
                if (input.Result == ExportResult.Success) { guard.GuardState = ExportGuardState.AwaitingReturn; guard.ChangedAt = clock.GetUtcNow(); guard.Version++; }
                else await guards.DeleteAsync(guard, token);
                await AuditExportAsync(slip, request.RequestId, hash, actor, "ITEM_RESULT_RECORDED", null, line, token);
            }
            slip.Version++;
            if (slip.Items.All(l => l.Result is not null))
            {
                slip.Status = ServiceWorkflowStatus.Completed; slip.CompletedAt = clock.GetUtcNow();
                await AuditExportAsync(slip, request.RequestId, hash, actor, "EXPORT_COMPLETED", ServiceWorkflowStatus.InProgress, null, token);
            }
            return true;
        }, ct);

    private async Task ValidateExportItemAsync(ServiceOrder order, ServiceOrderItem item, Guid? ownLineId, CancellationToken ct)
    {
        RequireStatus(order.Status, ServiceWorkflowStatus.InProgress);
        if (item.ExecutionMethod != ExecutionMethod.External || item.ExecutionStatus != ServiceItemStatus.InProgress) throw InvalidTransition();
        var device = await unitOfWork.Devices.GetByIdAsync(item.DeviceId, ct) ?? throw Missing("DEVICE_NOT_FOUND");
        if (device.Quantity != 1 || device.Status != EntityStatus.Active || device.Condition == DeviceCondition.Missing
            || device.DepartmentId != await equipment.RequireConfiguredAsync(ct))
            throw Conflict("DEVICE_NOT_EXPORTABLE", "Device must be available in the equipment department.");
        if (await unitOfWork.DeviceOperationHolds.ExistsForDeviceAsync(item.DeviceId, ct)) throw Busy();
        if (!await holds.ExistsAsync(h => h.DeviceId == item.DeviceId && h.ServiceOrderId == order.Id && h.ServiceOrderItemId == item.Id, ct)) throw Busy();
        var guard = await guards.FirstOrDefaultAsync(g => g.DeviceId == item.DeviceId, ct);
        if (ownLineId is null ? guard is not null : guard is null || guard.ExportSlipItemId != ownLineId || guard.GuardState != ExportGuardState.HeldForExport) throw Busy();
    }
    private async Task AddExportLineAsync(ExportSlip slip, ServiceOrderItem item, CancellationToken ct)
    {
        var line = new ExportSlipItem { ExportSlipId = slip.Id, ServiceOrderItemId = item.Id, DeviceCodeSnapshot = item.DeviceCodeSnapshot,
            DeviceNameSnapshot = item.DeviceNameSnapshot, SerialSnapshot = item.SerialSnapshot };
        slip.Items.Add(line);
        await guards.AddAsync(new ExportDeviceGuard { DeviceId = item.DeviceId, ExportSlipItemId = line.Id,
            GuardState = ExportGuardState.HeldForExport, AcquiredAt = clock.GetUtcNow(), ChangedAt = clock.GetUtcNow() }, ct);
    }
    private async Task<ExportDeviceGuard> OwnGuardAsync(ServiceOrderItem item, ExportSlipItem line, ExportGuardState state, CancellationToken ct)
        => await guards.FirstOrDefaultTrackedAsync(g => g.DeviceId == item.DeviceId && g.ExportSlipItemId == line.Id && g.GuardState == state, ct) ?? throw Busy();
    private async Task RemoveGuardAsync(ServiceOrderItem item, ExportSlipItem line, CancellationToken ct)
        => await guards.DeleteAsync(await OwnGuardAsync(item, line, ExportGuardState.HeldForExport, ct), ct);
    private async Task RequireExportEditorAsync(ExportSlip slip, Guid actor, CancellationToken ct)
    {
        var account = await unitOfWork.Accounts.GetByIdAsync(actor, ct) ?? throw Forbidden();
        var role = await unitOfWork.Roles.GetByIdAsync(account.RoleId, ct);
        if (slip.CreatedByAccountId != actor && role?.Code != RoleCodes.EquipmentManager) throw Forbidden();
    }
    private async Task<bool> ExportRetryAsync(ExportSlip slip, Guid request, string hash, Guid actor, string action, CancellationToken ct)
    {
        var events = await exportHistory.FindAsync(h => h.ExportSlipId == slip.Id && h.RequestId == request, ct);
        if (events.Count == 0) return false;
        if (!events.Any(h => h.ActionCode == action) || events.Any(h => h.ActorAccountId != actor || h.RequestPayloadHash != hash)) throw IdempotencyConflict();
        return true;
    }
    private Task AuditExportAsync(ExportSlip slip, Guid request, string hash, Guid actor, string action, object? before,
        ExportSlipItem? line, CancellationToken ct, AuditOutcome outcome = AuditOutcome.Success)
        => exportHistory.AddAsync(new ExportHistory { RequestId = request, RequestPayloadHash = hash, ExportSlipId = slip.Id,
            ExportSlipItemId = line?.Id, TargetKey = line is null ? "header" : "item:" + line.Id, ActorAccountId = actor,
            ActionCode = action, Outcome = outcome, OccurredAt = clock.GetUtcNow(),
            BeforeData = before is null ? null : JsonSerializer.Serialize(before),
            AfterData = line is null ? JsonSerializer.Serialize(ExportSnapshot(slip)) : JsonSerializer.Serialize(new {
                exportCode = slip.DisplayCode, lineId = line.Id, deviceCode = line.DeviceCodeSnapshot, result = line.Result.ToString(), failureReason = line.FailureReason }) }, ct);
    private static object ExportSnapshot(ExportSlip e) => new { exportCode = e.DisplayCode, status = e.Status.ToString(), e.Version,
        facility = e.FacilityNameSnapshot, plannedAt = e.PlannedExportAt, e.Notes, e.CancelReason,
        items = e.Items.OrderBy(i => i.Id).Select(i => new { lineId = i.Id, sourceItemId = i.ServiceOrderItemId, deviceCode = i.DeviceCodeSnapshot }).ToArray() };
    private static ExportSlip Export(ServiceOrder order, string code) => order.ExportSlips.Single(e => e.DisplayCode == Code(code));
    private static ServiceOrderItem[] SelectItems(ServiceOrder order, IEnumerable<int> numbers)
        => numbers.Select(n => order.Items.SingleOrDefault(i => i.ItemNo == n) ?? throw Validation("Unknown source item number.")).ToArray();
    private static void ValidateSelection(IEnumerable<int>? numbers)
    {
        var values = numbers?.ToArray();
        if (values is null || values.Length is < 1 or > 100 || values.Any(n => n is < 1 or > 100) || values.Distinct().Count() != values.Length)
            throw Validation("Select 1 to 100 distinct item numbers.");
    }
    private static void RequireFacility(string? value) { if (Clean(value) is null) throw Validation("Facility name is required."); }
    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    private static ExportSlipDto ToExportDto(ExportSlip e, ServiceOrder o, IReadOnlyDictionary<Guid, string> codes)
        => new(e.DisplayCode, o.DisplayCode, o.OrderType, e.SequenceNo, e.Status, e.FacilityNameSnapshot,
            e.FacilityAddressSnapshot, e.FacilityContactSnapshot, e.PlannedExportAt, e.Notes,
            ActorCode(codes, e.CreatedByAccountId)!, e.CreatedAt, ActorCode(codes, e.ApprovedByAccountId), e.ApprovedAt,
            ActorCode(codes, e.LastEditedByAccountId), e.LastEditedAt, ActorCode(codes, e.CanceledByAccountId), e.CanceledAt,
            e.CancelReason, e.CompletedAt, e.Version,
            e.Items.Select(l => new ExportSlipItemDto(o.Items.Single(i => i.Id == l.ServiceOrderItemId).ItemNo, l.DeviceCodeSnapshot,
                l.DeviceNameSnapshot, l.SerialSnapshot, l.Result, ActorCode(codes, l.ResultRecordedByAccountId), l.ResultRecordedAt,
                l.RecipientName, l.RecipientContact, l.FailureReason, l.EvidenceRef, l.Version, l.ReturnItem is not null)
                {
                    ReturnSlipCode = l.ReturnItem?.ReturnSlip.DisplayCode,
                    ReceivedByAccountCode = ActorCode(codes, l.ReturnItem?.ReturnSlip.ReceivedByAccountId),
                    ReceivedAt = l.ReturnItem?.ReturnSlip.ReceivedAt,
                    ConditionAtReturn = l.ReturnItem?.ConditionAtReturn,
                    ReturnNote = l.ReturnItem?.ReturnNote,
                    GuardState = l.ReturnItem is not null || l.Result == ExportResult.Failed || e.Status == ServiceWorkflowStatus.Canceled ? null
                        : l.Result == ExportResult.Success ? ExportGuardState.AwaitingReturn : ExportGuardState.HeldForExport
                }).OrderBy(i => i.ItemNo).ToArray());
}
