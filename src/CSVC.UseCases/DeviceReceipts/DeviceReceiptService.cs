using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.RegularExpressions;
using CSVC.Core.Accounts;
using CSVC.Core.Common;
using CSVC.Core.Departments;
using CSVC.Core.DeviceCategories;
using CSVC.Core.DeviceReceipts;
using CSVC.Core.Devices;
using CSVC.Core.Suppliers;
using CSVC.UseCases.Abstractions;
using CSVC.UseCases.Abstractions.Persistence;
using CSVC.UseCases.Abstractions.Security;
using CSVC.UseCases.Common;
using CSVC.UseCases.DeviceReceipts.DTOs;
using CSVC.UseCases.DeviceReceipts.Models;
using CSVC.UseCases.Exceptions;

namespace CSVC.UseCases.DeviceReceipts;

public sealed class DeviceReceiptService(
    IUnitOfWork unitOfWork, IInventoryTransaction transaction, ICurrentAccount current,
    IRequestContext requestContext, TimeProvider clock) : IDeviceReceiptService
{
    private static readonly Regex DeviceCodePattern = new("^[A-Z0-9][A-Z0-9._/-]{0,31}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public async Task<PagedResult<DeviceReceiptDto>> ListAsync(DeviceReceiptListQuery query, CancellationToken ct)
    {
        await RequireActorAsync(false, ct);
        Validate(query);
        if (query.FromDate > query.ToDate) throw Validation("fromDate cannot be later than toDate.");
        var page = await unitOfWork.DeviceReceipts.ListAsync(query, ct);
        return new(page.Items.Select(x => ToDto(x)).ToArray(), page.TotalCount, page.Page, page.PageSize);
    }

    public async Task<DeviceReceiptDto> GetAsync(string receiptCode, CancellationToken ct)
    {
        await RequireActorAsync(false, ct);
        var read = await unitOfWork.DeviceReceipts.GetByCodeAsync(NormalizeReceiptCode(receiptCode), ct) ?? throw Missing();
        return await ToDetailAsync(read, ct);
    }

    public async Task<DeviceReceiptDto> CreateAsync(CreateDeviceReceiptRequest request, CancellationToken ct)
        => await transaction.ExecuteAsync(async token =>
        {
            var actor = await RequireStaffAsync(token);
            Validate(request);
            var (supplier, department, items) = await ValidateHeaderAndBuildItemsAsync(request, [], token);
            var receipt = new DeviceReceipt
            {
                ReceiptCode = BusinessCode.NewRandom("PN"),
                SupplierId = supplier.Id, CarrierName = Clean(request.CarrierName), RequestCode = Clean(request.RequestCode),
                DeliveryDate = request.DeliveryDate, DeliveryBatch = TextNormalization.Name(request.DeliveryBatch),
                RequestingDepartmentId = department.Id, PaidInvoiceCode = Clean(request.PaidInvoiceCode),
                CreatorAccountId = actor.AccountId, CreatedAt = clock.GetUtcNow(), Status = DeviceReceiptStatus.PendingApproval,
                Notes = Clean(request.Notes), Items = items
            };
            foreach (var item in items)
                await unitOfWork.DeviceIdentities.AddAsync(new DeviceIdentity { DeviceCode = item.DeviceCode, Barcode = item.Barcode, ReservedAt = clock.GetUtcNow() }, token);
            await unitOfWork.DeviceReceipts.AddAsync(receipt, token);
            await ReceiptAuditAsync(receipt, actor.AccountId, "RECEIPT_CREATED", ["header", "items"], null, Snapshot(receipt, supplier.DisplayCode, department.Code, items), token);
            await unitOfWork.CommitAsync(token);
            var read = await unitOfWork.DeviceReceipts.GetByCodeAsync(receipt.ReceiptCode, token) ?? throw Missing();
            return await ToDetailAsync(read, token);
        }, ct);

    public async Task UpdateAsync(string receiptCode, UpdateDeviceReceiptRequest request, CancellationToken ct)
        => await transaction.ExecuteAsync(async token =>
        {
            var actor = await RequireStaffAsync(token);
            Validate(request);
            var key = NormalizeReceiptCode(receiptCode);
            var receipt = await unitOfWork.DeviceReceipts.FirstOrDefaultTrackedAsync(x => x.ReceiptCode == key, token) ?? throw Missing();
            RequirePending(receipt);
            if (receipt.CreatorAccountId != actor.AccountId) throw Forbidden("Only the receipt creator may edit it.");
            var oldItems = await unitOfWork.DeviceReceiptItems.ListTrackedForReceiptAsync(receipt.Id, token);
            var before = Snapshot(receipt, await SupplierCodeAsync(receipt.SupplierId, token), await DepartmentCodeAsync(receipt.RequestingDepartmentId, token), oldItems);
            var (supplier, department, items) = await ValidateHeaderAndBuildItemsAsync(request, oldItems, token);
            var retainedIds = items.Select(x => x.Id).ToHashSet();
            foreach (var old in oldItems.Where(x => !retainedIds.Contains(x.Id))) await unitOfWork.DeviceReceiptItems.DeleteAsync(old, token);
            foreach (var item in items)
            {
                item.ReceiptId = receipt.Id;
                if (!oldItems.Any(old => old.Id == item.Id)) await unitOfWork.DeviceReceiptItems.AddAsync(item, token);
            }
            foreach (var item in items.Where(item => !oldItems.Any(old => old.Id == item.Id)))
                await unitOfWork.DeviceIdentities.AddAsync(new DeviceIdentity { DeviceCode = item.DeviceCode, Barcode = item.Barcode, ReservedAt = clock.GetUtcNow() }, token);
            receipt.SupplierId = supplier.Id;
            receipt.CarrierName = Clean(request.CarrierName);
            receipt.RequestCode = Clean(request.RequestCode);
            receipt.DeliveryDate = request.DeliveryDate;
            receipt.DeliveryBatch = TextNormalization.Name(request.DeliveryBatch);
            receipt.RequestingDepartmentId = department.Id;
            receipt.PaidInvoiceCode = Clean(request.PaidInvoiceCode);
            receipt.Notes = Clean(request.Notes);
            unitOfWork.DeviceReceipts.Update(receipt);
            await ReceiptAuditAsync(receipt, actor.AccountId, "RECEIPT_UPDATED", ["header", "items"], before, Snapshot(receipt, supplier.DisplayCode, department.Code, items), token);
            await unitOfWork.CommitAsync(token);
            return true;
        }, ct);

    public async Task DecideAsync(string receiptCode, bool approve, ReceiptDecisionRequest request, CancellationToken ct)
        => await transaction.ExecuteAsync(async token =>
        {
            var actor = await RequireManagerAsync(token);
            Validate(request);
            var receipt = await unitOfWork.DeviceReceipts.FirstOrDefaultTrackedAsync(x => x.ReceiptCode == NormalizeReceiptCode(receiptCode), token) ?? throw Missing();
            RequirePending(receipt);
            var items = await unitOfWork.DeviceReceiptItems.FindAsync(x => x.ReceiptId == receipt.Id, token);
            if (items.Count == 0) throw Conflict("RECEIPT_EMPTY", "A receipt must contain at least one item.");
            var beforeDecision = SnapshotDecision(receipt);
            if (approve)
            {
                foreach (var item in items)
                {
                    var acquired = await unitOfWork.DeviceOperationHolds.TryAcquireAsync(new DeviceOperationHold
                    {
                        DeviceCode = item.DeviceCode, OperationKind = DeviceOperationKind.Receipt,
                        OperationId = receipt.Id, HeldAt = clock.GetUtcNow()
                    }, token);
                    if (!acquired) throw Conflict("DEVICE_BUSY", "An item is already held by another operation.");
                }
                receipt.Status = DeviceReceiptStatus.Approved;
            }
            else
            {
                receipt.Status = DeviceReceiptStatus.Rejected;
                foreach (var item in items)
                    await unitOfWork.DeviceOperationHolds.ReleaseAsync(item.DeviceCode, DeviceOperationKind.Receipt, receipt.Id, token);
            }
            receipt.DecisionAccountId = actor.AccountId;
            receipt.DecidedAt = clock.GetUtcNow();
            receipt.DecisionReason = Clean(request.Reason);
            unitOfWork.DeviceReceipts.Update(receipt);
            await ReceiptAuditAsync(receipt, actor.AccountId, approve ? "RECEIPT_APPROVED" : "RECEIPT_REJECTED", ["status", "decisionAccount", "decidedAt", "decisionReason"], beforeDecision, SnapshotDecision(receipt), token);
            await unitOfWork.CommitAsync(token);
            return true;
        }, ct);

    public async Task CompleteAsync(string receiptCode, CancellationToken ct)
        => await transaction.ExecuteAsync(async token =>
        {
            var actor = await RequireStaffAsync(token);
            var receipt = await unitOfWork.DeviceReceipts.FirstOrDefaultTrackedAsync(x => x.ReceiptCode == NormalizeReceiptCode(receiptCode), token) ?? throw Missing();
            if (receipt.Status != DeviceReceiptStatus.Approved) throw Conflict("INVALID_WORKFLOW_TRANSITION", "Only an approved receipt can be completed.");
            var items = await unitOfWork.DeviceReceiptItems.FindAsync(x => x.ReceiptId == receipt.Id, token);
            if (items.Count == 0 || items.Any(x => x.DeviceId is not null)) throw Conflict("INVALID_WORKFLOW_TRANSITION", "Receipt items are invalid or already completed.");
            var created = new List<Device>();
            var departmentCode = await DepartmentCodeAsync(receipt.RequestingDepartmentId, token);
            var deviceByCode = new Dictionary<string, Device>(StringComparer.Ordinal);
            foreach (var item in items)
            {
                var device = new Device
                {
                    Id = Guid.NewGuid(), DeviceCode = item.DeviceCode, Barcode = item.Barcode, Name = item.Name,
                    CategoryId = item.CategoryId, SerialNumber = item.SerialNumber,
                    DepartmentId = receipt.RequestingDepartmentId, Status = EntityStatus.Active, Condition = DeviceCondition.Good
                };
                deviceByCode.Add(device.DeviceCode, device);
                item.DeviceId = device.Id;
                unitOfWork.DeviceReceiptItems.Update(item);
                await unitOfWork.Devices.AddAsync(device, token);
                created.Add(device);
            }
            foreach (var item in items)
            {
                var device = deviceByCode[item.DeviceCode];
                if (!string.IsNullOrWhiteSpace(item.ParentDeviceCode))
                {
                    if (deviceByCode.TryGetValue(item.ParentDeviceCode, out var pendingParent)) device.ParentDeviceId = pendingParent.Id;
                    else
                    {
                        var parent = await unitOfWork.Devices.FirstOrDefaultAsync(x => x.DeviceCode == item.ParentDeviceCode, token)
                            ?? throw new UseCaseException(FailureKind.NotFound, "PARENT_DEVICE_NOT_FOUND", "Parent device was not found.");
                        if (parent.DepartmentId != receipt.RequestingDepartmentId) throw Conflict("DEVICE_PARENT_DEPARTMENT_MISMATCH", "Parent and child must belong to the receipt department.");
                        device.ParentDeviceId = parent.Id;
                    }
                }
                await unitOfWork.DeviceHistories.AddAsync(new DeviceHistory
                {
                    RequestId = requestContext.RequestId, ActorAccountId = actor.AccountId, DeviceId = device.Id,
                    ActionCode = "DEVICE_RECEIVED_" + device.DeviceCode, Outcome = AuditOutcome.Success, OccurredAt = clock.GetUtcNow(),
                    ChangedFields = "[\"deviceCode\",\"barcode\",\"name\",\"categoryCode\",\"departmentCode\"]",
                    AfterData = JsonSerializer.Serialize(new { device.DeviceCode, device.Barcode, item.Name, receiptCode = receipt.ReceiptCode, departmentCode })
                }, token);
                var released = await unitOfWork.DeviceOperationHolds.ReleaseAsync(item.DeviceCode, DeviceOperationKind.Receipt, receipt.Id, token);
                if (!released) throw Conflict("DEVICE_BUSY", "The receipt no longer owns its device reservation.");
            }
            var before = SnapshotDecision(receipt);
            receipt.Status = DeviceReceiptStatus.Completed;
            receipt.CompletedAt = clock.GetUtcNow();
            unitOfWork.DeviceReceipts.Update(receipt);
            await ReceiptAuditAsync(receipt, actor.AccountId, "RECEIPT_COMPLETED", ["status", "completedAt", "items"], before,
                JsonSerializer.Serialize(new { receipt.ReceiptCode, status = receipt.Status.ToString(), receipt.CompletedAt, deviceCodes = created.Select(x => x.DeviceCode) }), token);
            await unitOfWork.CommitAsync(token);
            return true;
        }, ct);

    public async Task CancelAsync(string receiptCode, CancellationToken ct)
        => await transaction.ExecuteAsync(async token =>
        {
            var actor = await RequireStaffAsync(token);
            var receipt = await unitOfWork.DeviceReceipts.FirstOrDefaultTrackedAsync(x => x.ReceiptCode == NormalizeReceiptCode(receiptCode), token) ?? throw Missing();
            RequirePending(receipt);
            if (receipt.CreatorAccountId != actor.AccountId) throw Forbidden("Only the receipt creator may cancel it.");
            var before = SnapshotDecision(receipt);
            receipt.Status = DeviceReceiptStatus.Cancelled;
            unitOfWork.DeviceReceipts.Update(receipt);
            await ReceiptAuditAsync(receipt, actor.AccountId, "RECEIPT_CANCELLED", ["status"], before, SnapshotDecision(receipt), token);
            await unitOfWork.CommitAsync(token);
            return true;
        }, ct);

    public async Task<PagedResult<DeviceReceiptHistoryDto>> HistoryAsync(string receiptCode, DeviceReceiptHistoryQuery query, CancellationToken ct)
    {
        await RequireActorAsync(false, ct);
        Validate(query);
        var receipt = await unitOfWork.DeviceReceipts.GetByCodeAsync(NormalizeReceiptCode(receiptCode), ct) ?? throw Missing();
        var page = await unitOfWork.DeviceReceiptHistories.ListForReceiptAsync(receipt.Id, query, ct);
        return new(page.Items.Select(x => new DeviceReceiptHistoryDto(x.ActionCode, x.Outcome.ToString(), x.OccurredAt, x.ActorAccountCode,
            x.ActorFullName, x.ReasonCode, x.ChangedFields, x.BeforeData, x.AfterData)).ToArray(), page.TotalCount, page.Page, page.PageSize);
    }

    private async Task<(Supplier Supplier, Department Department, List<DeviceReceiptItem> Items)> ValidateHeaderAndBuildItemsAsync(
        CreateDeviceReceiptRequest request, IReadOnlyList<DeviceReceiptItem> existingItems, CancellationToken ct)
    {
        if (request.DeliveryDate == DateOnly.MinValue) throw Validation("deliveryDate is required.");
        if (string.IsNullOrWhiteSpace(request.DeliveryBatch)) throw Validation("deliveryBatch is required.");
        if (string.IsNullOrWhiteSpace(request.SupplierCode) || string.IsNullOrWhiteSpace(request.RequestingDepartmentCode))
            throw Validation("supplierCode and requestingDepartmentCode are required.");
        if (request.Items is null || request.Items.Count == 0) throw Validation("At least one device item is required.");
        var supplier = await FindByCodeAsync(unitOfWork.Suppliers, request.SupplierCode, x => x.DisplayCode, "SUPPLIER_NOT_FOUND", ct);
        var department = await FindByCodeAsync(unitOfWork.Departments, request.RequestingDepartmentCode, x => x.Code, "DEPARTMENT_NOT_FOUND", ct);
        if (supplier.Status != EntityStatus.Active) throw Conflict("SUPPLIER_UNAVAILABLE", "The selected supplier is locked.");
        if (department.Status != EntityStatus.Active) throw Conflict("DEPARTMENT_UNAVAILABLE", "The selected department is locked.");

        var codes = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<DeviceReceiptItem>(request.Items.Count);
        foreach (var input in request.Items)
        {
            if (input is null) throw Validation("Receipt items cannot contain null values.");
            if (string.IsNullOrWhiteSpace(input.CategoryCode) || string.IsNullOrWhiteSpace(input.Name))
                throw Validation("Every item requires categoryCode and name.");
            var category = await FindByCodeAsync(unitOfWork.DeviceCategories, input.CategoryCode, x => x.DisplayCode, "DEVICE_CATEGORY_NOT_FOUND", ct);
            if (category.Status != EntityStatus.Active) throw Conflict("DEVICE_CATEGORY_UNAVAILABLE", "A selected category is locked.");
            var name = TextNormalization.Name(input.Name ?? "");
            if (name.Length == 0) throw Validation("Device name is required.");
            var requestedCode = string.IsNullOrWhiteSpace(input.DeviceCode) ? null : input.DeviceCode.Trim().ToUpperInvariant();
            var code = requestedCode ?? BusinessCode.NewRandom("TB");
            if (code.Length > 32 || !DeviceCodePattern.IsMatch(code)) throw Validation("Device code format is invalid.");
            if (!codes.Add(code)) throw Conflict("DEVICE_CODE_CONFLICT", "The receipt contains a duplicate device code.");
            var existing = existingItems.FirstOrDefault(x => x.DeviceCode == code);
            if (existing is null && await unitOfWork.DeviceIdentities.ExistsAsync(x => x.DeviceCode == code, ct))
                throw Conflict("DEVICE_CODE_CONFLICT", "The device code has already been reserved.");
            var item = existing ?? new DeviceReceiptItem { DeviceCode = code, Barcode = BusinessCode.NewRandom("BC"), Name = name };
            item.CategoryId = category.Id;
            item.Name = name;
            item.DeviceCode = code;
            item.DeviceCodeSource = requestedCode is null ? DeviceCodeSource.Generated : existing?.DeviceCodeSource ?? DeviceCodeSource.Manual;
            item.SerialNumber = Clean(input.SerialNumber);
            item.ParentDeviceCode = string.IsNullOrWhiteSpace(input.ParentDeviceCode) ? null : input.ParentDeviceCode.Trim().ToUpperInvariant();
            if (item.ParentDeviceCode == code) throw Validation("A device cannot be its own parent.");
            result.Add(item);
        }
        var itemCodes = result.Select(x => x.DeviceCode).ToHashSet(StringComparer.Ordinal);
        foreach (var item in result.Where(x => x.ParentDeviceCode is not null))
            if (!itemCodes.Contains(item.ParentDeviceCode!) && await unitOfWork.Devices.FirstOrDefaultAsync(x => x.DeviceCode == item.ParentDeviceCode, ct) is null)
                throw new UseCaseException(FailureKind.NotFound, "PARENT_DEVICE_NOT_FOUND", "Parent device was not found.");
        var parentsByCode = result.ToDictionary(x => x.DeviceCode, x => x.ParentDeviceCode, StringComparer.Ordinal);
        foreach (var item in result)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var cursor = item.DeviceCode;
            while (parentsByCode.TryGetValue(cursor, out var next) && next is not null)
            {
                if (!visited.Add(cursor) || next == item.DeviceCode) throw Conflict("INVALID_DEVICE_PARENT", "Receipt device relationships cannot contain a cycle.");
                cursor = next;
            }
        }
        return (supplier, department, result);
    }

    private async Task<Actor> RequireActorAsync(bool managerOnly, CancellationToken ct)
    {
        if (current.AccountId is not { } id || current.SessionId is not { } sessionId) throw Unauthorized();
        var account = await unitOfWork.Accounts.FirstOrDefaultAsync(x => x.Id == id && !x.IsLock, ct);
        var session = await unitOfWork.AuthSessions.FirstOrDefaultAsync(x => x.Id == sessionId && x.AccountId == id && x.IsActive && x.ExpiresAt > clock.GetUtcNow(), ct);
        if (account is null || session is null) throw Unauthorized();
        var role = await unitOfWork.Roles.GetByIdAsync(account.RoleId, ct);
        var permitted = role?.Code is RoleCodes.EquipmentManager or RoleCodes.EquipmentStaff;
        if (!permitted || managerOnly && role!.Code != RoleCodes.EquipmentManager) throw Forbidden("Receipt access is not permitted.");
        return new(id, role!.Code);
    }

    private async Task<Actor> RequireStaffAsync(CancellationToken ct)
    {
        var actor = await RequireActorAsync(false, ct);
        if (actor.RoleCode != RoleCodes.EquipmentStaff) throw Forbidden("Only equipment staff may perform this action.");
        return actor;
    }

    private Task<Actor> RequireManagerAsync(CancellationToken ct) => RequireActorAsync(true, ct);

    private async Task<string> SupplierCodeAsync(Guid id, CancellationToken ct)
        => (await unitOfWork.Suppliers.GetByIdAsync(id, ct))?.DisplayCode ?? "";
    private async Task<string> DepartmentCodeAsync(Guid id, CancellationToken ct)
        => (await unitOfWork.Departments.GetByIdAsync(id, ct))?.Code ?? "";

    private async Task<T> FindByCodeAsync<T>(IRepository<T> repository, string code, System.Linq.Expressions.Expression<Func<T, string>> selector, string errorCode, CancellationToken ct) where T : class
    {
        var value = code.Trim().ToLowerInvariant();
        var body = System.Linq.Expressions.Expression.Equal(System.Linq.Expressions.Expression.Call(selector.Body, typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!), System.Linq.Expressions.Expression.Constant(value));
        return await repository.FirstOrDefaultAsync(System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(body, selector.Parameters[0]), ct)
            ?? throw new UseCaseException(FailureKind.NotFound, errorCode, "The selected related record was not found.");
    }

    private async Task<DeviceReceiptDto> ToDetailAsync(DeviceReceiptReadModel read, CancellationToken ct)
    {
        var items = await unitOfWork.DeviceReceiptItems.ListForReceiptAsync(read.Id, ct);
        return ToDto(read) with { Items = items.Select(x => new DeviceReceiptItemDto(x.CategoryCode, x.CategoryName, x.Name, x.DeviceCode,
            x.Barcode, x.DeviceCodeSource.ToString(), x.SerialNumber, x.Completed, x.ParentDeviceCode)).ToArray() };
    }

    private static DeviceReceiptDto ToDto(DeviceReceiptReadModel x)
        => new(x.ReceiptCode, x.SupplierCode, x.SupplierName, x.CarrierName, x.RequestCode, x.DeliveryDate, x.DeliveryBatch,
            x.DepartmentCode, x.DepartmentName, x.PaidInvoiceCode, x.Status, x.CreatorAccountCode, x.CreatedAt,
            x.DecisionAccountCode, x.DecidedAt, x.CompletedAt, x.DecisionReason, x.Notes, x.ItemCount, x.Version);

    private Task ReceiptAuditAsync(DeviceReceipt receipt, Guid actor, string action, IReadOnlyList<string> fields, string? before, string after, CancellationToken ct)
        => unitOfWork.DeviceReceiptHistories.AddAsync(new DeviceReceiptHistory
        {
            RequestId = requestContext.RequestId, ActorAccountId = actor, ReceiptId = receipt.Id, ActionCode = action,
            Outcome = AuditOutcome.Success, OccurredAt = clock.GetUtcNow(), ChangedFields = JsonSerializer.Serialize(fields),
            BeforeData = before, AfterData = after
        }, ct);

    private static void RequirePending(DeviceReceipt receipt)
    {
        if (receipt.Status != DeviceReceiptStatus.PendingApproval) throw Conflict("INVALID_WORKFLOW_TRANSITION", "Only a pending receipt can be changed or decided.");
    }

    private static string Snapshot(DeviceReceipt receipt, string supplierCode, string departmentCode, IEnumerable<DeviceReceiptItem> items)
        => JsonSerializer.Serialize(new { receipt.ReceiptCode, supplierCode, receipt.CarrierName, receipt.RequestCode, receipt.DeliveryDate, receipt.DeliveryBatch,
            departmentCode, receipt.PaidInvoiceCode, receipt.Notes, items = items.Select(x => new { x.DeviceCode, x.Barcode, x.Name, x.SerialNumber }) });
    private static string SnapshotDecision(DeviceReceipt receipt)
        => JsonSerializer.Serialize(new { receipt.ReceiptCode, status = receipt.Status.ToString(), receipt.DecisionReason, receipt.DecidedAt, receipt.CompletedAt });
    private static string NormalizeReceiptCode(string value) => value.Trim().ToUpperInvariant();
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : TextNormalization.Name(value);
    private static void Validate(object request)
    {
        if (!Validator.TryValidateObject(request, new ValidationContext(request), [], true)) throw Validation("Request validation failed.");
    }
    private static UseCaseException Unauthorized() => new(FailureKind.Unauthorized, "AUTHENTICATION_REQUIRED", "A valid session is required.");
    private static UseCaseException Forbidden(string message) => new(FailureKind.Forbidden, "WORKFLOW_ACTION_FORBIDDEN", message);
    private static UseCaseException Missing() => new(FailureKind.NotFound, "RECEIPT_NOT_FOUND", "Receipt was not found.");
    private static UseCaseException Validation(string message) => new(FailureKind.Validation, "VALIDATION_FAILED", message);
    private static UseCaseException Conflict(string code, string message) => new(FailureKind.Conflict, code, message);
    private sealed record Actor(Guid AccountId, string RoleCode);
}
