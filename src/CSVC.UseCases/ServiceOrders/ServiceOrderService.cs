using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CSVC.Core.Accounts;
using CSVC.Core.Common;
using CSVC.Core.Devices;
using CSVC.Core.ServiceOrders;
using CSVC.UseCases.Abstractions.Persistence;
using CSVC.UseCases.Abstractions.Security;
using CSVC.UseCases.Common;
using CSVC.UseCases.Exceptions;
using CSVC.UseCases.ServiceOrders.DTOs;

namespace CSVC.UseCases.ServiceOrders;

public sealed class ServiceOrderService(
    IServiceOrderRepository orders, IRepository<ServiceOrderHistory> history, IRepository<ExportHistory> exportHistory,
    IUnitOfWork unitOfWork, IInventoryTransaction transaction, ICurrentAccount current, TimeProvider clock) : IServiceOrderService
{
    public async Task<PagedResult<ServiceOrderDto>> ListAsync(ServiceOrderListQuery query, CancellationToken ct)
    {
        await RequireActorAsync(null, ct);
        Validate(query);
        var page = await orders.ListAsync(query, ct);
        var codes = await AccountCodesAsync(page.Items, ct);
        return new(page.Items.Select(x => ToDto(x, codes)).ToArray(), page.TotalCount, page.Page, page.PageSize);
    }

    public async Task<ServiceOrderDto> GetAsync(string code, CancellationToken ct)
    {
        await RequireActorAsync(null, ct);
        var order = await LoadAsync(code, false, ct);
        return ToDto(order, await AccountCodesAsync([order], ct));
    }

    public Task<ServiceOrderDto> CreateAsync(CreateServiceOrderRequest request, CancellationToken ct)
        => transaction.ExecuteAsync(async token =>
        {
            var actor = await RequireActorAsync(RoleCodes.EquipmentStaff, token);
            ValidateCreate(request);
            var hash = Fingerprint(request);
            var existing = await orders.GetByCreateRequestAsync(request.RequestId, token);
            if (existing is not null)
            {
                if (existing.CreatedByAccountId != actor || existing.CreateRequestHash != hash) throw IdempotencyConflict();
                return ToDto(existing, await AccountCodesAsync([existing], token));
            }
            var now = clock.GetUtcNow();
            var order = new ServiceOrder
            {
                DisplayCode = BusinessCode.NewRandom(request.OrderType == ServiceOrderType.Maintenance ? "BT" : "SC"),
                OrderType = request.OrderType, CreateRequestId = request.RequestId, CreateRequestHash = hash,
                CreatedByAccountId = actor, CreatedAt = now, Notes = Clean(request.Notes)
            };
            var deviceCodes = request.Items.Select(x => Code(x.DeviceCode)).ToArray();
            var devices = await unitOfWork.Devices.FindAsync(x => deviceCodes.Contains(x.DeviceCode), token);
            if (devices.Count != deviceCodes.Length) throw Missing("DEVICE_NOT_FOUND");
            await transaction.LockDevicesAsync(devices.Select(x => x.Id), token);
            foreach (var (input, index) in request.Items.Select((x, i) => (x, i)))
            {
                var device = devices.Single(x => x.DeviceCode == Code(input.DeviceCode));
                await ValidateDeviceAsync(device, order.OrderType, token);
                var item = new ServiceOrderItem
                {
                    ServiceOrderId = order.Id, ItemNo = index + 1, DeviceId = device.Id,
                    ExecutionMethod = input.ExecutionMethod, WorkDescription = Clean(input.WorkDescription), Notes = Clean(input.Notes),
                    DeviceCodeSnapshot = device.DeviceCode, DeviceNameSnapshot = device.Name, SerialSnapshot = device.SerialNumber
                };
                if (order.OrderType == ServiceOrderType.Repair) item.RepairDetail = new() { ServiceOrderItemId = item.Id };
                order.Items.Add(item);
            }
            foreach (var (input, index) in request.ExportSlips.Select((x, i) => (x, i)))
            {
                var slip = new ExportSlip
                {
                    ServiceOrderId = order.Id, SequenceNo = index + 1, DisplayCode = BusinessCode.NewRandom("PX"),
                    FacilityNameSnapshot = Clean(input.FacilityName)!, FacilityAddressSnapshot = Clean(input.FacilityAddress),
                    FacilityContactSnapshot = Clean(input.FacilityContact), PlannedExportAt = input.PlannedExportAt?.ToUniversalTime(),
                    Notes = Clean(input.Notes), CreatedByAccountId = actor, CreatedAt = now
                };
                foreach (var deviceCode in input.DeviceCodes)
                {
                    var item = order.Items.Single(x => x.DeviceCodeSnapshot == Code(deviceCode));
                    slip.Items.Add(new ExportSlipItem
                    {
                        ExportSlipId = slip.Id, ServiceOrderItemId = item.Id,
                        DeviceCodeSnapshot = item.DeviceCodeSnapshot, DeviceNameSnapshot = item.DeviceNameSnapshot, SerialSnapshot = item.SerialSnapshot
                    });
                }
                order.ExportSlips.Add(slip);
                await AuditExportAsync(slip, request.RequestId, actor, "EXPORT_CREATED", null, token);
            }
            await orders.AddAsync(order, token);
            await AuditAsync(order, request.RequestId, actor, "SERVICE_CREATED", null, null, null, token);
            return ToDto(order, await AccountCodesAsync([order], token));
        }, ct);

    public Task ApproveAsync(string code, ServiceTransitionRequest request, CancellationToken ct)
        => transaction.ExecuteAsync(async token =>
        {
            var actor = await RequireActorAsync(RoleCodes.EquipmentManager, token);
            Validate(request); RequireRequest(request.RequestId);
            var order = await LoadAsync(code, true, token);
            if (order.ApprovalRequestId == request.RequestId)
            {
                if (order.ApprovedByAccountId != actor) throw IdempotencyConflict();
                return true;
            }
            RequireVersion(order.Version, request.Version);
            RequireStatus(order.Status, ServiceWorkflowStatus.PendingApproval);
            await transaction.LockDevicesAsync(order.Items.Select(x => x.DeviceId), token);
            var now = clock.GetUtcNow();
            foreach (var item in order.Items)
            {
                var device = await unitOfWork.Devices.GetByIdAsync(item.DeviceId, token) ?? throw Missing("DEVICE_NOT_FOUND");
                await ValidateDeviceAsync(device, order.OrderType, token);
                if (!await unitOfWork.DeviceOperationHolds.TryAcquireAsync(new DeviceOperationHold
                    { DeviceCode = device.DeviceCode, DeviceId = device.Id, OperationKind = DeviceOperationKind.ServiceOrder,
                      OperationId = order.Id, HeldAt = now }, token)) throw Busy();
                item.ExecutionStatus = ServiceItemStatus.InProgress;
                item.Version++;
            }
            order.Status = ServiceWorkflowStatus.InProgress; order.ApprovedByAccountId = actor;
            order.ApprovedAt = now; order.ApprovalRequestId = request.RequestId; order.Version++;
            foreach (var slip in order.ExportSlips)
            {
                RequireStatus(slip.Status, ServiceWorkflowStatus.PendingApproval);
                slip.Status = ServiceWorkflowStatus.InProgress; slip.Version++;
                await AuditExportAsync(slip, request.RequestId, actor, "EXPORT_STARTED", ServiceWorkflowStatus.PendingApproval, token);
            }
            await AuditAsync(order, request.RequestId, actor, "SERVICE_APPROVED", ServiceWorkflowStatus.PendingApproval, null, null, token);
            return true;
        }, ct);

    public Task CancelAsync(string code, CancelServiceOrderRequest request, CancellationToken ct)
        => transaction.ExecuteAsync(async token =>
        {
            var actor = await RequireActorAsync(RoleCodes.EquipmentStaff, token);
            Validate(request); RequireRequest(request.RequestId);
            var reason = Clean(request.Reason) ?? throw Validation("A cancel reason is required.");
            var order = await LoadAsync(code, true, token);
            if (order.CreatedByAccountId != actor) throw Forbidden();
            if (order.CancelRequestId == request.RequestId)
            {
                if (order.CanceledByAccountId != actor || order.CancelReason != reason) throw IdempotencyConflict();
                return true;
            }
            RequireVersion(order.Version, request.Version);
            RequireStatus(order.Status, ServiceWorkflowStatus.PendingApproval);
            order.Status = ServiceWorkflowStatus.Canceled; order.CanceledByAccountId = actor;
            order.CanceledAt = clock.GetUtcNow(); order.CancelRequestId = request.RequestId; order.CancelReason = reason; order.Version++;
            foreach (var slip in order.ExportSlips)
            {
                RequireStatus(slip.Status, ServiceWorkflowStatus.PendingApproval);
                slip.Status = ServiceWorkflowStatus.Canceled; slip.CanceledAt = order.CanceledAt; slip.Version++;
                await AuditExportAsync(slip, request.RequestId, actor, "CANCELED_BY_SOURCE", ServiceWorkflowStatus.PendingApproval, token);
            }
            await AuditAsync(order, request.RequestId, actor, "SERVICE_CANCELED", ServiceWorkflowStatus.PendingApproval, null, null, token);
            return true;
        }, ct);

    public Task RecordResultsAsync(string code, RecordServiceResultsRequest request, CancellationToken ct)
        => transaction.ExecuteAsync(async token =>
        {
            var actor = await RequireActorAsync(RoleCodes.EquipmentStaff, token);
            Validate(request); RequireRequest(request.RequestId);
            if (request.Items is null || request.Items.Count is < 1 or > 100) throw Validation("Supply 1 to 100 results.");
            foreach (var input in request.Items)
            {
                if (input is null) throw Validation("Result items cannot be null.");
                Validate(input);
                if (input.Result is not (ServiceItemStatus.Completed or ServiceItemStatus.Failed))
                    throw Validation("A professional result must be Completed or Failed.");
                if (Clean(input.Description) is null) throw Validation("A result description is required.");
            }
            if (request.Items.Select(x => x.ItemNo).Distinct().Count() != request.Items.Count) throw Validation("Duplicate result item numbers.");
            var order = await LoadAsync(code, true, token);
            var prior = order.Items.Where(x => x.ResultRequestId == request.RequestId).ToArray();
            if (prior.Length > 0)
            {
                if (prior.Length != request.Items.Count || request.Items.Any(input => !prior.Any(x => x.ItemNo == input.ItemNo
                    && x.ExecutionStatus == input.Result && x.ResultDescription == Clean(input.Description) && x.ResultRecordedByAccountId == actor)))
                    throw IdempotencyConflict();
                return true;
            }
            RequireVersion(order.Version, request.Version);
            RequireStatus(order.Status, ServiceWorkflowStatus.InProgress);
            var selected = request.Items.Select(input => order.Items.SingleOrDefault(x => x.ItemNo == input.ItemNo)
                ?? throw Validation("The result item does not belong to this order.")).ToArray();
            await transaction.LockDevicesAsync(selected.Select(x => x.DeviceId), token);
            foreach (var item in selected)
            {
                if (item.ExecutionStatus != ServiceItemStatus.InProgress) throw InvalidTransition();
                if (item.ExecutionMethod == ExecutionMethod.External && !order.ExportSlips.Any(e => e.Result == ExportResult.Success
                    && e.Items.Any(line => line.ServiceOrderItemId == item.Id)))
                    throw Conflict("EXPORT_NOT_SUCCESSFUL", "Record a successful export before recording external service results.");
                var input = request.Items.Single(x => x.ItemNo == item.ItemNo);
                item.ExecutionStatus = input.Result; item.ResultDescription = Clean(input.Description);
                item.ResultRecordedByAccountId = actor; item.ResultRecordedAt = clock.GetUtcNow();
                item.ResultRequestId = request.RequestId; item.Version++;
                await ReleaseAsync(order, item, token);
                await AuditAsync(order, request.RequestId, actor, input.Result == ServiceItemStatus.Completed ? "ITEM_COMPLETED" : "ITEM_FAILED",
                    ServiceItemStatus.InProgress, item, null, token);
            }
            order.Version++;
            await CompleteIfFinishedAsync(order, request.RequestId, actor, null, token);
            return true;
        }, ct);

    public async Task<PagedResult<ExportSlipDto>> ListExportsAsync(ExportSlipListQuery query, CancellationToken ct)
    {
        await RequireActorAsync(null, ct); Validate(query);
        return await orders.ListExportsAsync(query, ct);
    }

    public async Task<ExportSlipDto> GetExportAsync(string code, CancellationToken ct)
    {
        await RequireActorAsync(null, ct);
        var order = await LoadExportAsync(code, false, ct);
        return ToExportDto(order.ExportSlips.Single(x => x.DisplayCode == Code(code)), order, await AccountCodesAsync([order], ct));
    }

    public Task RecordExportResultAsync(string code, RecordExportResultRequest request, CancellationToken ct)
        => transaction.ExecuteAsync(async token =>
        {
            var actor = await RequireActorAsync(RoleCodes.EquipmentStaff, token);
            Validate(request); RequireRequest(request.RequestId);
            if (request.Result == ExportResult.Success && (Clean(request.RecipientName) is null || Clean(request.FailureReason) is not null))
                throw Validation("Successful export requires a recipient and no failure reason.");
            if (request.Result == ExportResult.Failed && (Clean(request.FailureReason) is null || Clean(request.RecipientName) is not null || Clean(request.RecipientContact) is not null))
                throw Validation("Failed export requires a reason and no recipient.");
            if (Clean(request.EvidenceRef) is { } evidence && Uri.TryCreate(evidence, UriKind.Absolute, out _))
                throw Validation("evidenceRef must be an opaque document reference, not an absolute URL.");
            var order = await LoadExportAsync(code, true, token);
            var slip = order.ExportSlips.Single(x => x.DisplayCode == Code(code));
            if (slip.ResultRequestId == request.RequestId)
            {
                if (slip.ResultRecordedByAccountId != actor || slip.Result != request.Result || slip.RecipientName != Clean(request.RecipientName)
                    || slip.RecipientContact != Clean(request.RecipientContact) || slip.FailureReason != Clean(request.FailureReason)
                    || slip.EvidenceRef != Clean(request.EvidenceRef)) throw IdempotencyConflict();
                return true;
            }
            RequireVersion(slip.Version, request.Version);
            RequireStatus(order.Status, ServiceWorkflowStatus.InProgress);
            RequireStatus(slip.Status, ServiceWorkflowStatus.InProgress);
            var ids = slip.Items.Select(x => x.ServiceOrderItemId).ToHashSet();
            var items = order.Items.Where(x => ids.Contains(x.Id)).ToArray();
            if (items.Length != ids.Count || items.Length == 0 || items.Any(x => x.ExecutionMethod != ExecutionMethod.External
                || x.ExecutionStatus != ServiceItemStatus.InProgress)) throw InvalidTransition();
            await transaction.LockDevicesAsync(items.Select(x => x.DeviceId), token);
            slip.Result = request.Result; slip.ResultRequestId = request.RequestId; slip.ResultRecordedByAccountId = actor;
            slip.ResultRecordedAt = clock.GetUtcNow(); slip.CompletedAt = slip.ResultRecordedAt;
            slip.Status = ServiceWorkflowStatus.Completed; slip.Version++;
            slip.RecipientName = Clean(request.RecipientName); slip.RecipientContact = Clean(request.RecipientContact);
            slip.FailureReason = Clean(request.FailureReason); slip.EvidenceRef = Clean(request.EvidenceRef);
            await AuditExportAsync(slip, request.RequestId, actor,
                request.Result == ExportResult.Success ? "EXPORT_RESULT_SUCCESS" : "EXPORT_RESULT_FAILED", ServiceWorkflowStatus.InProgress, token);
            if (request.Result == ExportResult.Failed)
            {
                foreach (var item in items)
                {
                    item.ExecutionStatus = ServiceItemStatus.NotExecuted; item.Version++;
                    await ReleaseAsync(order, item, token);
                    await AuditAsync(order, request.RequestId, actor, "ITEM_NOT_EXECUTED", ServiceItemStatus.InProgress, item, slip, token);
                }
                order.Version++;
                await CompleteIfFinishedAsync(order, request.RequestId, actor, slip, token);
            }
            return true;
        }, ct);

    public async Task<PagedResult<ServiceHistoryDto>> HistoryAsync(string code, ServiceHistoryQuery query, CancellationToken ct)
    {
        await RequireActorAsync(null, ct); Validate(query);
        return await orders.HistoryAsync((await LoadAsync(code, false, ct)).Id, query, ct);
    }

    public async Task<PagedResult<ServiceHistoryDto>> ExportHistoryAsync(string code, ServiceHistoryQuery query, CancellationToken ct)
    {
        await RequireActorAsync(null, ct); Validate(query);
        var order = await LoadExportAsync(code, false, ct);
        return await orders.ExportHistoryAsync(order.ExportSlips.Single(x => x.DisplayCode == Code(code)).Id, query, ct);
    }

    private async Task<Guid> RequireActorAsync(string? requiredRole, CancellationToken ct)
    {
        if (current.AccountId is not { } id || current.SessionId is not { } sessionId) throw Unauthorized();
        var account = await unitOfWork.Accounts.FirstOrDefaultAsync(x => x.Id == id && !x.IsLock, ct);
        var session = await unitOfWork.AuthSessions.FirstOrDefaultAsync(x => x.Id == sessionId && x.AccountId == id
            && x.IsActive && x.ExpiresAt > clock.GetUtcNow(), ct);
        if (account is null || session is null) throw Unauthorized();
        var role = await unitOfWork.Roles.GetByIdAsync(account.RoleId, ct);
        if (role?.Code is not (RoleCodes.EquipmentManager or RoleCodes.EquipmentStaff)
            || (requiredRole is not null && role.Code != requiredRole)) throw Forbidden();
        return id;
    }

    private async Task ValidateDeviceAsync(Device device, ServiceOrderType type, CancellationToken ct)
    {
        if (device.Status != EntityStatus.Active) throw Conflict("DEVICE_LOCKED", "The device is locked.");
        if (device.Condition == DeviceCondition.Missing || (type == ServiceOrderType.Maintenance && device.Condition != DeviceCondition.Good))
            throw Conflict("DEVICE_NOT_AVAILABLE", "The device condition is incompatible with this service order.");
        if (await unitOfWork.DeviceOperationHolds.ExistsForDeviceAsync(device.Id, ct)) throw Busy();
    }

    private async Task ReleaseAsync(ServiceOrder order, ServiceOrderItem item, CancellationToken ct)
    {
        if (!await unitOfWork.DeviceOperationHolds.ReleaseAsync(item.DeviceCodeSnapshot, DeviceOperationKind.ServiceOrder, order.Id, ct)) throw Busy();
    }

    private async Task CompleteIfFinishedAsync(ServiceOrder order, Guid requestId, Guid actor, ExportSlip? cause, CancellationToken ct)
    {
        if (order.Items.Any(x => x.ExecutionStatus is null or ServiceItemStatus.InProgress)) return;
        order.Status = ServiceWorkflowStatus.Completed; order.CompletedAt = clock.GetUtcNow();
        await AuditAsync(order, requestId, actor, "ORDER_COMPLETED", ServiceWorkflowStatus.InProgress, null, cause, ct);
    }

    private Task AuditAsync(ServiceOrder order, Guid requestId, Guid actor, string action, object? before,
        ServiceOrderItem? item, ExportSlip? cause, CancellationToken ct)
        => history.AddAsync(new ServiceOrderHistory
        {
            RequestId = requestId, ServiceOrderId = order.Id, ServiceOrderItemId = item?.Id, RelatedExportSlipId = cause?.Id,
            TargetKey = item is null ? "ORDER" : "ITEM:" + item.Id, ActorAccountId = actor,
            ActionCode = action, Outcome = AuditOutcome.Success, OccurredAt = clock.GetUtcNow(),
            BeforeData = before is null ? null : JsonSerializer.Serialize(new { status = before.ToString() }),
            AfterData = JsonSerializer.Serialize(new { orderCode = order.DisplayCode, itemNo = item?.ItemNo,
                status = item is null ? order.Status.ToString() : item.ExecutionStatus?.ToString(),
                resultDescription = item?.ResultDescription, relatedExportSlipCode = cause?.DisplayCode, cancelReason = order.CancelReason })
        }, ct);

    private Task AuditExportAsync(ExportSlip slip, Guid requestId, Guid actor, string action, ServiceWorkflowStatus? before, CancellationToken ct)
        => exportHistory.AddAsync(new ExportHistory
        {
            RequestId = requestId, ExportSlipId = slip.Id, ActorAccountId = actor, ActionCode = action,
            Outcome = AuditOutcome.Success, OccurredAt = clock.GetUtcNow(),
            BeforeData = before is null ? null : JsonSerializer.Serialize(new { status = before.ToString() }),
            AfterData = JsonSerializer.Serialize(new { exportCode = slip.DisplayCode, status = slip.Status.ToString(),
                result = slip.Result?.ToString(), facilityName = slip.FacilityNameSnapshot, failureReason = slip.FailureReason })
        }, ct);

    private async Task<ServiceOrder> LoadAsync(string code, bool tracked, CancellationToken ct)
        => await orders.GetAggregateAsync(Code(code), tracked, ct) ?? throw Missing("SERVICE_ORDER_NOT_FOUND");
    private async Task<ServiceOrder> LoadExportAsync(string code, bool tracked, CancellationToken ct)
        => await orders.GetByExportCodeAsync(Code(code), tracked, ct) ?? throw Missing("EXPORT_SLIP_NOT_FOUND");

    private Task<IReadOnlyDictionary<Guid, string>> AccountCodesAsync(IEnumerable<ServiceOrder> source, CancellationToken ct)
        => orders.AccountCodesAsync(source.SelectMany(o => new Guid?[] { o.CreatedByAccountId, o.ApprovedByAccountId, o.CanceledByAccountId }
            .Concat(o.Items.Select(i => i.ResultRecordedByAccountId)).Concat(o.ExportSlips.Select(e => e.ResultRecordedByAccountId)))
            .Where(x => x.HasValue).Select(x => x!.Value).Distinct(), ct);
    private static string? ActorCode(IReadOnlyDictionary<Guid, string> codes, Guid? id)
        => id is { } value && codes.TryGetValue(value, out var code) ? code : null;

    private static ServiceOrderDto ToDto(ServiceOrder o, IReadOnlyDictionary<Guid, string> codes)
        => new(o.DisplayCode, o.OrderType, o.Status, ActorCode(codes, o.CreatedByAccountId)!, o.CreatedAt,
            ActorCode(codes, o.ApprovedByAccountId), o.ApprovedAt, ActorCode(codes, o.CanceledByAccountId), o.CanceledAt,
            o.CancelReason, o.CompletedAt, o.Notes, o.Version, o.Items.Count,
            o.Items.OrderBy(i => i.ItemNo).Select(i => new ServiceOrderItemDto(i.ItemNo, i.DeviceCodeSnapshot, i.DeviceNameSnapshot,
                i.SerialSnapshot, i.ExecutionMethod, i.ExecutionStatus, i.WorkDescription, i.ResultDescription,
                ActorCode(codes, i.ResultRecordedByAccountId), i.ResultRecordedAt, i.Notes, i.Version)).ToArray(),
            o.ExportSlips.OrderBy(e => e.SequenceNo).Select(e => ToExportDto(e, o, codes)).ToArray());

    private static ExportSlipDto ToExportDto(ExportSlip e, ServiceOrder o, IReadOnlyDictionary<Guid, string> codes)
        => new(e.DisplayCode, o.DisplayCode, o.OrderType, e.SequenceNo, e.Status, e.FacilityNameSnapshot,
            e.FacilityAddressSnapshot, e.FacilityContactSnapshot, e.PlannedExportAt, e.Notes,
            ActorCode(codes, e.CreatedByAccountId)!, e.CreatedAt, e.Result, ActorCode(codes, e.ResultRecordedByAccountId),
            e.ResultRecordedAt, e.RecipientName, e.RecipientContact, e.FailureReason, e.EvidenceRef,
            e.CanceledAt, e.CompletedAt, e.Version,
            e.Items.Select(line => new ExportSlipItemDto(o.Items.Single(i => i.Id == line.ServiceOrderItemId).ItemNo,
                line.DeviceCodeSnapshot, line.DeviceNameSnapshot, line.SerialSnapshot)).OrderBy(i => i.ItemNo).ToArray());

    private static void ValidateCreate(CreateServiceOrderRequest request)
    {
        Validate(request); RequireRequest(request.RequestId);
        if (request.Items is null || request.ExportSlips is null || request.Items.Count is < 1 or > 100 || request.ExportSlips.Count > 100)
            throw Validation("Supply 1 to 100 items and at most 100 export slips.");
        foreach (var item in request.Items)
        {
            if (item is null) throw Validation("Items cannot be null.");
            Validate(item);
            if (!Regex.IsMatch(Code(item.DeviceCode), BusinessCode.Pattern, RegexOptions.CultureInvariant) || Code(item.DeviceCode).Length > 32)
                throw Validation("Invalid device code.");
        }
        var all = request.Items.Select(x => Code(x.DeviceCode)).ToArray();
        if (all.Distinct().Count() != all.Length) throw Validation("Duplicate devices.");
        var exported = new List<string>();
        foreach (var slip in request.ExportSlips)
        {
            if (slip is null) throw Validation("Export slips cannot be null.");
            Validate(slip);
            if (Clean(slip.FacilityName) is null || slip.DeviceCodes is null || slip.DeviceCodes.Count is < 1 or > 100)
                throw Validation("Every export requires a facility name and devices.");
            exported.AddRange(slip.DeviceCodes.Select(Code));
        }
        var external = request.Items.Where(x => x.ExecutionMethod == ExecutionMethod.External).Select(x => Code(x.DeviceCode)).ToHashSet();
        if (exported.Distinct().Count() != exported.Count || !external.SetEquals(exported))
            throw Validation("Every external item must occur in exactly one export slip; on-site items cannot be exported.");
    }

    private static string Fingerprint(CreateServiceOrderRequest r)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            r.OrderType, notes = Clean(r.Notes),
            items = r.Items.Select(i => new { deviceCode = Code(i.DeviceCode), i.ExecutionMethod,
                workDescription = Clean(i.WorkDescription), notes = Clean(i.Notes) }),
            exports = r.ExportSlips.Select(e => new { facilityName = Clean(e.FacilityName), facilityAddress = Clean(e.FacilityAddress),
                facilityContact = Clean(e.FacilityContact), plannedExportAt = e.PlannedExportAt?.ToUniversalTime(), notes = Clean(e.Notes),
                deviceCodes = e.DeviceCodes.Select(Code).Order().ToArray() })
        }))));
    private static void Validate(object value)
    {
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(value, new ValidationContext(value), results, true))
            throw Validation(results[0].ErrorMessage ?? "Invalid request.");
    }
    private static string Code(string? value) => value?.Trim().ToUpperInvariant() ?? "";
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().Normalize();
    private static void RequireRequest(Guid id) { if (id == Guid.Empty) throw Validation("requestId must be a non-empty UUID."); }
    private static void RequireVersion(int actual, int? expected) { if (expected is { } version && actual != version) throw Conflict("VERSION_CONFLICT", "Reload the current version."); }
    private static void RequireStatus(ServiceWorkflowStatus actual, ServiceWorkflowStatus required) { if (actual != required) throw InvalidTransition(); }
    private static UseCaseException Validation(string text) => new(FailureKind.Validation, "VALIDATION_FAILED", text);
    private static UseCaseException Unauthorized() => new(FailureKind.Unauthorized, "AUTHENTICATION_REQUIRED", "An active session is required.");
    private static UseCaseException Forbidden() => new(FailureKind.Forbidden, "WORKFLOW_ACTION_FORBIDDEN", "This action is not permitted for this account.");
    private static UseCaseException Missing(string code) => new(FailureKind.NotFound, code, "The requested record was not found.");
    private static UseCaseException Conflict(string code, string text) => new(FailureKind.Conflict, code, text);
    private static UseCaseException Busy() => Conflict("DEVICE_BUSY", "A device reservation conflicts with this operation.");
    private static UseCaseException IdempotencyConflict() => Conflict("IDEMPOTENCY_CONFLICT", "The request ID was already used with different data or a different account.");
    private static UseCaseException InvalidTransition() => Conflict("INVALID_WORKFLOW_TRANSITION", "The operation is not allowed in the current state.");
}
