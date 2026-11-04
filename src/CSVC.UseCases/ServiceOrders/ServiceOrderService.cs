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

public sealed partial class ServiceOrderService(
    IServiceOrderRepository orders, IRepository<ServiceOrderHistory> history, IRepository<ExportHistory> exportHistory,
    IUnitOfWork unitOfWork, IInventoryTransaction transaction, ICurrentAccount current, TimeProvider clock,
    IRepository<ServiceOrderDeviceHold> holds, IRepository<ExportDeviceGuard> guards,
    IRepository<ExportSlipItem> exportItems,
    CSVC.UseCases.Settings.IEquipmentDepartmentPolicy equipment, IRepository<CSVC.Core.DamageReports.DamageReport> damageReports) : IServiceOrderService
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
            var actor = await RequireActorAsync(null, token);
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
                if (Clean(input.DamageReportCode) is { } reportCode)
                {
                    if (order.OrderType != ServiceOrderType.Repair) throw Validation("Only Repair items may reference a damage report.");
                    var report = await damageReports.FirstOrDefaultTrackedAsync(r => r.ReportCode == Code(reportCode) && r.DeviceId == device.Id, token)
                        ?? throw Validation("Damage report must exist and concern this device.");
                    item.RepairDetail = new() { ServiceOrderItemId = item.Id, DamageReportId = report.Id, DamageReport = report };
                }
                else if (order.OrderType == ServiceOrderType.Repair) item.RepairDetail = new() { ServiceOrderItemId = item.Id };
                order.Items.Add(item);
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
                if (await guards.ExistsAsync(x => x.DeviceId == device.Id, token)) throw Busy();
                await holds.AddAsync(new ServiceOrderDeviceHold { DeviceId = device.Id, ServiceOrderId = order.Id,
                    ServiceOrderItemId = item.Id, HeldByAccountId = actor, HeldAt = now }, token);
                item.ExecutionStatus = ServiceItemStatus.InProgress;
                item.Version++;
                await AuditAsync(order, request.RequestId, actor, "HOLD_ACQUIRED", null, item, null, token);
            }
            order.Status = ServiceWorkflowStatus.InProgress; order.ApprovedByAccountId = actor;
            order.ApprovedAt = now; order.ApprovalRequestId = request.RequestId; order.Version++;
            await AuditAsync(order, request.RequestId, actor, "SERVICE_APPROVED", ServiceWorkflowStatus.PendingApproval, null, null, token);
            return true;
        }, ct);

    public Task CancelAsync(string code, CancelServiceOrderRequest request, CancellationToken ct)
        => transaction.ExecuteAsync(async token =>
        {
            var actor = await RequireActorAsync(null, token);
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
            await AuditAsync(order, request.RequestId, actor, "SERVICE_CANCELED", ServiceWorkflowStatus.PendingApproval, null, null, token);
            return true;
        }, ct);

    public Task RecordResultsAsync(string code, RecordServiceResultsRequest request, CancellationToken ct)
        => transaction.ExecuteAsync(async token =>
        {
            var actor = await RequireActorAsync(null, token);
            Validate(request); RequireRequest(request.RequestId);
            if (request.Items is null || request.Items.Count is < 1 or > 100) throw Validation("Supply 1 to 100 results.");
            foreach (var input in request.Items)
            {
                if (input is null) throw Validation("Result items cannot be null.");
                Validate(input);
                if (input.Result is not (ServiceItemStatus.Completed or ServiceItemStatus.Failed or ServiceItemStatus.NotExecuted))
                    throw Validation("A professional result must be Completed, Failed or NotExecuted.");
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
                var guard = await guards.FirstOrDefaultAsync(g => g.DeviceId == item.DeviceId, token);
                if (guard?.GuardState == ExportGuardState.HeldForExport)
                    throw Conflict("EXPORT_RESULT_PENDING", "Record the pending export result before the professional outcome.");
                var inputResult = request.Items.Single(x => x.ItemNo == item.ItemNo).Result;
                if (item.ExecutionMethod == ExecutionMethod.External && inputResult != ServiceItemStatus.NotExecuted
                    && !order.ExportSlips.Any(e => e.Items.Any(line => line.ServiceOrderItemId == item.Id && line.Result == ExportResult.Success)))
                    throw Conflict("EXPORT_NOT_SUCCESSFUL", "A successful export is required for external work; select NotExecuted with a reason otherwise.");
                var input = request.Items.Single(x => x.ItemNo == item.ItemNo);
                item.ExecutionStatus = input.Result; item.ResultDescription = Clean(input.Description);
                item.ResultRecordedByAccountId = actor; item.ResultRecordedAt = clock.GetUtcNow();
                item.ResultRequestId = request.RequestId; item.Version++;
                await ReleaseAsync(order, item, request.RequestId, actor, token);
                await AuditAsync(order, request.RequestId, actor, "ITEM_" + input.Result.ToString().ToUpperInvariant(),
                    ServiceItemStatus.InProgress, item, null, token);
            }
            order.Version++;
            await CompleteIfFinishedAsync(order, request.RequestId, actor, null, token);
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
        var profile = await unitOfWork.UserProfiles.GetByIdAsync(account.UserProfileId, ct) ?? throw Forbidden();
        await equipment.RequireMemberAsync(role.Code, profile.DepartmentId, ct);
        return id;
    }

    private async Task ValidateDeviceAsync(Device device, ServiceOrderType type, CancellationToken ct)
    {
        if (device.Quantity != 1) throw Conflict("DEVICE_NOT_RECEIVED", "The device has not been received yet.");
        if (device.Status != EntityStatus.Active) throw Conflict("DEVICE_LOCKED", "The device is locked.");
        if (device.Condition == DeviceCondition.Missing || (type == ServiceOrderType.Maintenance && device.Condition != DeviceCondition.Good))
            throw Conflict("DEVICE_NOT_AVAILABLE", "The device condition is incompatible with this service order.");
        if (await unitOfWork.DeviceOperationHolds.ExistsForDeviceAsync(device.Id, ct)
            || await holds.ExistsAsync(x => x.DeviceId == device.Id, ct)
            || await guards.ExistsAsync(x => x.DeviceId == device.Id, ct)) throw Busy();
    }

    private async Task<bool> ReleaseAsync(ServiceOrder order, ServiceOrderItem item, Guid requestId, Guid actor, CancellationToken ct)
    {
        if (item.ExecutionStatus is null or ServiceItemStatus.InProgress || await guards.ExistsAsync(g => g.DeviceId == item.DeviceId, ct)) return false;
        var hold = await holds.FirstOrDefaultTrackedAsync(h => h.DeviceId == item.DeviceId && h.ServiceOrderItemId == item.Id && h.ServiceOrderId == order.Id, ct);
        if (hold is null) return false;
        await holds.DeleteAsync(hold, ct);
        await AuditAsync(order, requestId, actor, "HOLD_RELEASED", new { deviceCode = item.DeviceCodeSnapshot, heldAt = hold.HeldAt }, item, null, ct);
        return true;
    }

    public Task<int> ReconcileHoldsAsync(string code, ServiceTransitionRequest request, CancellationToken ct)
        => transaction.ExecuteAsync(async token =>
        {
            var actor = await RequireActorAsync(null, token);
            Validate(request); RequireRequest(request.RequestId);
            var order = await LoadAsync(code, true, token);
            var prior = await history.FindAsync(h => h.ServiceOrderId == order.Id && h.RequestId == request.RequestId && h.ActionCode == "HOLDS_RECONCILED", token);
            if (prior.Count != 0)
            {
                if (prior[0].ActorAccountId != actor) throw IdempotencyConflict();
                return JsonDocument.Parse(prior[0].AfterData!).RootElement.GetProperty("released").GetInt32();
            }
            RequireVersion(order.Version, request.Version);
            await transaction.LockDevicesAsync(order.Items.Select(i => i.DeviceId), token);
            var released = 0;
            foreach (var item in order.Items)
                if (await ReleaseAsync(order, item, request.RequestId, actor, token)) released++;
            await history.AddAsync(new ServiceOrderHistory { ServiceOrderId = order.Id, RequestId = request.RequestId,
                TargetKey = "ORDER", ActorAccountId = actor, ActionCode = "HOLDS_RECONCILED", Outcome = released == 0 ? AuditOutcome.NoChange : AuditOutcome.Success,
                OccurredAt = clock.GetUtcNow(), AfterData = JsonSerializer.Serialize(new { released }) }, token);
            return released;
        }, ct);

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
            BeforeData = before is null ? null : JsonSerializer.Serialize(before),
            AfterData = JsonSerializer.Serialize(new { orderCode = order.DisplayCode, itemNo = item?.ItemNo,
                status = item is null ? order.Status.ToString() : item.ExecutionStatus?.ToString(),
                deviceCode = item?.DeviceCodeSnapshot, resultDescription = item?.ResultDescription, relatedExportSlipCode = cause?.DisplayCode, cancelReason = order.CancelReason })
        }, ct);

    private async Task<ServiceOrder> LoadAsync(string code, bool tracked, CancellationToken ct)
        => await orders.GetAggregateAsync(Code(code), tracked, ct) ?? throw Missing("SERVICE_ORDER_NOT_FOUND");
    private async Task<ServiceOrder> LoadExportAsync(string code, bool tracked, CancellationToken ct)
        => await orders.GetByExportCodeAsync(Code(code), tracked, ct) ?? throw Missing("EXPORT_SLIP_NOT_FOUND");

    private Task<IReadOnlyDictionary<Guid, string>> AccountCodesAsync(IEnumerable<ServiceOrder> source, CancellationToken ct)
        => orders.AccountCodesAsync(source.SelectMany(o => new Guid?[] { o.CreatedByAccountId, o.ApprovedByAccountId, o.CanceledByAccountId }
            .Concat(o.Items.Select(i => i.ResultRecordedByAccountId))
            .Concat(o.ExportSlips.SelectMany(e => new Guid?[] { e.CreatedByAccountId, e.ApprovedByAccountId, e.CanceledByAccountId, e.LastEditedByAccountId }
                .Concat(e.Items.Select(i => i.ResultRecordedByAccountId))
                .Concat(e.Items.Select(i => (Guid?)i.ReturnItem?.ReturnSlip.ReceivedByAccountId)))))
            .Where(x => x.HasValue).Select(x => x!.Value).Distinct(), ct);
    private static string? ActorCode(IReadOnlyDictionary<Guid, string> codes, Guid? id)
        => id is { } value && codes.TryGetValue(value, out var code) ? code : null;

    private ServiceOrderDto ToDto(ServiceOrder o, IReadOnlyDictionary<Guid, string> codes)
        => new(o.DisplayCode, o.OrderType, o.Status, ActorCode(codes, o.CreatedByAccountId)!, o.CreatedAt,
            ActorCode(codes, o.ApprovedByAccountId), o.ApprovedAt, ActorCode(codes, o.CanceledByAccountId), o.CanceledAt,
            o.CancelReason, o.CompletedAt, o.Notes, o.Version, o.Items.Count,
            o.Items.OrderBy(i => i.ItemNo).Select(i => new ServiceOrderItemDto(i.ItemNo, i.DeviceCodeSnapshot, i.DeviceNameSnapshot,
                i.SerialSnapshot, i.ExecutionMethod, i.ExecutionStatus, i.WorkDescription, i.ResultDescription,
                ActorCode(codes, i.ResultRecordedByAccountId), i.ResultRecordedAt, i.Notes, i.Version) { DamageReportCode = i.RepairDetail?.DamageReport?.ReportCode }).ToArray(),
            o.ExportSlips.OrderBy(e => e.SequenceNo).Select(e => ToExportDto(e, o, codes)).ToArray());

    private static void ValidateCreate(CreateServiceOrderRequest request)
    {
        Validate(request); RequireRequest(request.RequestId);
        if (request.Items is null || request.Items.Count is < 1 or > 100)
            throw Validation("Supply 1 to 100 items.");
        foreach (var item in request.Items)
        {
            if (item is null) throw Validation("Items cannot be null.");
            Validate(item);
            if (!Regex.IsMatch(Code(item.DeviceCode), BusinessCode.Pattern, RegexOptions.CultureInvariant) || Code(item.DeviceCode).Length > 32)
                throw Validation("Invalid device code.");
        }
        var all = request.Items.Select(x => Code(x.DeviceCode)).ToArray();
        if (all.Distinct().Count() != all.Length) throw Validation("Duplicate devices.");
    }

    private static string Fingerprint(CreateServiceOrderRequest r)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            r.OrderType, notes = Clean(r.Notes),
            items = r.Items.Select(i => new { deviceCode = Code(i.DeviceCode), i.ExecutionMethod,
                workDescription = Clean(i.WorkDescription), notes = Clean(i.Notes), damageReportCode = Clean(i.DamageReportCode) is null ? null : Code(i.DamageReportCode) })
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
