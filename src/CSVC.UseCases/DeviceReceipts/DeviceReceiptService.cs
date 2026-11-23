using CSVC.UseCases.ProcurementRequests;
using CSVC.Core.ProcurementRequests;
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
    IRequestContext requestContext, TimeProvider clock, CSVC.UseCases.Devices.IDeviceReservationPolicy reservations, IBusinessCodeGenerator codeGenerator, CSVC.UseCases.Settings.IEquipmentDepartmentPolicy equipment, IProcurementWorkflow procurement, IRequestAssignmentPolicy assignment) : IDeviceReceiptService
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
            var receivingId = await ReceivingDepartmentAsync(actor, null, token);
            var (supplier, department, items) = await ValidateHeaderAndBuildItemsAsync(request, [], false, token);
            var receipt = new DeviceReceipt
            {
                ReceiptCode = await codeGenerator.NextAsync(BusinessCodeKind.DeviceReceipt, token),
                SupplierId = supplier.Id, CarrierName = Clean(request.CarrierName), RequestCode = Clean(request.RequestCode),
                DeliveryDate = request.DeliveryDate, DeliveryBatch = TextNormalization.Name(request.DeliveryBatch),
                RequestingDepartmentId = department.Id, ReceivingDepartmentId = receivingId, WorkflowVersion = 2, PaidInvoiceCode = Clean(request.PaidInvoiceCode),
                CreatorAccountId = actor.AccountId, CreatedAt = clock.GetUtcNow(), Status = DeviceReceiptStatus.PendingApproval,
                Notes = Clean(request.Notes), Items = items
            };
            await ReserveIdentitiesAsync(items, token);
            await unitOfWork.DeviceReceipts.AddAsync(receipt, token);
            var sourceBindings = new List<RequestDeviceBinding>();
            for (var i = 0; i < items.Count; i++)
                sourceBindings.Add(new(request.Items[i].SourceLineCode ?? "", items[i].DeviceCode,
                    (await unitOfWork.DeviceCategories.GetByIdAsync(items[i].CategoryId, token))!.DisplayCode));
            await procurement.AttachAsync(request.SourceRequestCode, RequestDocumentKind.Receipt, receipt.ReceiptCode, department.Id, sourceBindings, actor.AccountId, token);
            await ReceiptAuditAsync(receipt, actor.AccountId, "RECEIPT_CREATED", ["header", "items"], null, Snapshot(receipt, supplier.DisplayCode, department.Code, items, receipt.ReceivingDepartmentId is { } receiver ? await DepartmentCodeAsync(receiver, token) : null), token);
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
            var linkedToSource = await assignment.RequireDocumentAsync(RequestDocumentKind.Receipt, receipt.ReceiptCode, actor.AccountId, token);
            RequirePending(receipt);
            if (receipt.CreatorAccountId != actor.AccountId && !linkedToSource) throw Forbidden("Only the receipt creator may edit it.");
            await procurement.PreventLinkedEditAsync(RequestDocumentKind.Receipt, receipt.ReceiptCode, token);
            if (!string.IsNullOrWhiteSpace(request.SourceRequestCode) || request.Items.Any(i => i is not null && !string.IsNullOrWhiteSpace(i.SourceLineCode)))
                throw Validation("Create a new document from the source request; an existing receipt cannot acquire source bindings through editing.");

            await ReceivingDepartmentAsync(actor, receipt, token);
            var oldItems = await unitOfWork.DeviceReceiptItems.ListTrackedForReceiptAsync(receipt.Id, token);
            var before = Snapshot(receipt, await SupplierCodeAsync(receipt.SupplierId, token), await DepartmentCodeAsync(receipt.RequestingDepartmentId, token), oldItems, receipt.ReceivingDepartmentId is { } receiverBefore ? await DepartmentCodeAsync(receiverBefore, token) : null);
            var (supplier, department, items) = await ValidateHeaderAndBuildItemsAsync(request, oldItems, receipt.WorkflowVersion == 1, token);
            var retainedIds = items.Select(x => x.Id).ToHashSet();
            foreach (var old in oldItems.Where(x => !retainedIds.Contains(x.Id))) await unitOfWork.DeviceReceiptItems.DeleteAsync(old, token);
            foreach (var item in items)
            {
                item.ReceiptId = receipt.Id;
                if (!oldItems.Any(old => old.Id == item.Id)) await unitOfWork.DeviceReceiptItems.AddAsync(item, token);
            }
            await ReserveIdentitiesAsync(items, token);
            receipt.SupplierId = supplier.Id;
            receipt.CarrierName = Clean(request.CarrierName);
            receipt.RequestCode = Clean(request.RequestCode);
            receipt.DeliveryDate = request.DeliveryDate;
            receipt.DeliveryBatch = TextNormalization.Name(request.DeliveryBatch);
            receipt.RequestingDepartmentId = department.Id;
            receipt.PaidInvoiceCode = Clean(request.PaidInvoiceCode);
            receipt.Notes = Clean(request.Notes);
            unitOfWork.DeviceReceipts.Update(receipt);
            await ReceiptAuditAsync(receipt, actor.AccountId, "RECEIPT_UPDATED", ["header", "items"], before, Snapshot(receipt, supplier.DisplayCode, department.Code, items, receipt.ReceivingDepartmentId is { } receiver ? await DepartmentCodeAsync(receiver, token) : null), token);
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
            await ReceivingDepartmentAsync(actor, receipt, token);
            var items = await unitOfWork.DeviceReceiptItems.FindAsync(x => x.ReceiptId == receipt.Id, token);
            if (items.Count == 0) throw Conflict("RECEIPT_EMPTY", "A receipt must contain at least one item.");
            var beforeDecision = SnapshotDecision(receipt);
            if (approve)
            {
                foreach (var item in items)
                {
                    var device = await unitOfWork.Devices.FirstOrDefaultAsync(x => x.DeviceCode == item.DeviceCode, token);
                    if (device is not null)
                    {
                        await transaction.LockDevicesAsync([device.Id], token);
                        RequireReceivable(device, receipt.WorkflowVersion == 1 ? receipt.RequestingDepartmentId : null);
                        await reservations.RequireAsync(device, false, ct: token);
                    }
                    var acquired = await unitOfWork.DeviceOperationHolds.TryAcquireAsync(new DeviceOperationHold
                    {
                        DeviceCode = item.DeviceCode, DeviceId = device?.Id, OperationKind = DeviceOperationKind.Receipt,
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
            var linkedToSource = await assignment.RequireDocumentAsync(RequestDocumentKind.Receipt, receipt.ReceiptCode, actor.AccountId, token);
            if (receipt.Status != DeviceReceiptStatus.Approved) throw Conflict("INVALID_WORKFLOW_TRANSITION", "Only an approved receipt can be completed.");
            var items = await unitOfWork.DeviceReceiptItems.FindAsync(x => x.ReceiptId == receipt.Id, token);
            if (items.Count == 0 || items.Any(x => x.DeviceId is not null)) throw Conflict("INVALID_WORKFLOW_TRANSITION", "Receipt items are invalid or already completed.");
            var created = new List<Device>();
            var receivingId = await ReceivingDepartmentAsync(actor, receipt, token);
            var departmentCode = await DepartmentCodeAsync(receivingId, token);
            var requestingDepartmentCode = await DepartmentCodeAsync(receipt.RequestingDepartmentId, token);
            var beforeStates = new Dictionary<string, string>();
            foreach (var item in items)
            {
                if (!await unitOfWork.DeviceOperationHolds.IsOwnedByAsync(item.DeviceCode, DeviceOperationKind.Receipt, receipt.Id, token))
                    throw Conflict("DEVICE_BUSY", "The receipt no longer owns its device reservation.");
                var device = await unitOfWork.Devices.FirstOrDefaultTrackedAsync(x => x.DeviceCode == item.DeviceCode, token);
                if (device is not null)
                {
                    await transaction.LockDevicesAsync([device.Id], token);
                    RequireReceivable(device, receipt.WorkflowVersion == 1 ? receipt.RequestingDepartmentId : null);
                    await reservations.RequireAsync(device, false, DeviceOperationKind.Receipt, receipt.Id, token);
                    beforeStates[item.DeviceCode] = JsonSerializer.Serialize(new { device.Quantity, departmentCode = await DepartmentCodeAsync(device.DepartmentId, token), useState = device.UseState.ToString() });
                    if (receipt.WorkflowVersion == 2)
                    {
                        if (device.DepartmentId != receivingId) { device.AreaId = null; device.ResponsibleAccountId = null; }
                        device.DepartmentId = receivingId;
                        device.ResponsibleAccountId ??= actor.AccountId;
                        device.UseState = DeviceUseState.InStorage;
                    }
                    device.Quantity = 1;
                    unitOfWork.Devices.Update(device);
                }
                else
                {
                    device = new Device
                    {
                        Id = Guid.NewGuid(), DeviceCode = item.DeviceCode, Barcode = item.Barcode, Name = item.Name,
                        CategoryId = item.CategoryId, SerialNumber = item.SerialNumber, Quantity = 1,
                        DepartmentId = receivingId, Status = EntityStatus.Active, Condition = DeviceCondition.Good,
                        UseState = receipt.WorkflowVersion == 2 ? DeviceUseState.InStorage : DeviceUseState.Unspecified,
                        ResponsibleAccountId = receipt.WorkflowVersion == 2 ? actor.AccountId : null
                    };
                    await unitOfWork.Devices.AddAsync(device, token);
                }
                item.DeviceId = device.Id;
                unitOfWork.DeviceReceiptItems.Update(item);
                created.Add(device);
            }
            foreach (var item in items)
            {
                var device = created.Single(d => d.DeviceCode == item.DeviceCode);
                await unitOfWork.DeviceHistories.AddAsync(new DeviceHistory
                {
                    RequestId = requestContext.RequestId, ActorAccountId = actor.AccountId, DeviceId = device.Id,
                    ActionCode = "DEVICE_RECEIVED_" + device.DeviceCode, Outcome = AuditOutcome.Success, OccurredAt = clock.GetUtcNow(),
                    ChangedFields = "[\"deviceCode\",\"barcode\",\"name\",\"categoryCode\",\"departmentCode\",\"quantity\",\"useState\",\"responsibleAccountCode\",\"areaCode\"]",
                    BeforeData = beforeStates.GetValueOrDefault(device.DeviceCode) ?? JsonSerializer.Serialize(new { quantity = 0 }),
                    AfterData = JsonSerializer.Serialize(new { device.DeviceCode, device.Barcode, device.Name, quantity = device.Quantity, receiptCode = receipt.ReceiptCode, departmentCode, requestingDepartmentCode, useState = device.UseState.ToString() })
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
            var linkedToSource = await assignment.RequireDocumentAsync(RequestDocumentKind.Receipt, receipt.ReceiptCode, actor.AccountId, token);
            RequirePending(receipt);
            if (receipt.CreatorAccountId != actor.AccountId && !linkedToSource) throw Forbidden("Only the receipt creator may cancel it.");

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
        CreateDeviceReceiptRequest request, IReadOnlyList<DeviceReceiptItem> existingItems, bool legacy, CancellationToken ct)
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

        var requestedCodes = request.Items
            .Select(item => BusinessCode.Normalize(item?.ExistingDeviceCode) ?? BusinessCode.Normalize(item?.DeviceCode))
            .OfType<string>().ToHashSet(StringComparer.Ordinal);
        var codes = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<DeviceReceiptItem>(request.Items.Count);
        foreach (var input in request.Items)
        {
            if (input is null) throw Validation("Receipt items cannot contain null values.");
            Validate(input);
            if (!string.IsNullOrWhiteSpace(input.ExistingDeviceCode) && !string.IsNullOrWhiteSpace(input.DeviceCode))
                throw Validation("Use existingDeviceCode to select a device or deviceCode to declare a new one.");
            var requestedCode = BusinessCode.Normalize(input.ExistingDeviceCode) ?? BusinessCode.Normalize(input.DeviceCode);
            var code = requestedCode ?? await codeGenerator.NextAsync(BusinessCodeKind.Device, ct, requestedCodes);
            if (code.Length > 32 || !DeviceCodePattern.IsMatch(code)) throw Validation("Device code format is invalid.");
            if (!codes.Add(code)) throw Conflict("DEVICE_CODE_CONFLICT", "The receipt contains a duplicate device code.");
            var existing = existingItems.FirstOrDefault(x => x.DeviceCode == code);
            var device = await unitOfWork.Devices.FirstOrDefaultAsync(x => x.DeviceCode == code, ct);
            if (!string.IsNullOrWhiteSpace(input.ExistingDeviceCode) && device is null)
                throw new UseCaseException(FailureKind.NotFound, "DEVICE_NOT_FOUND", "The selected device was not found.");
            if (device is not null) { RequireReceivable(device, legacy ? department.Id : null); await reservations.RequireAsync(device, false, ct: ct); }
            if (existing is null && device is null && await unitOfWork.DeviceIdentities.ExistsAsync(x => x.DeviceCode == code, ct))
                throw Conflict("DEVICE_CODE_CONFLICT", "The device code has already been reserved.");
            if (device is null && (string.IsNullOrWhiteSpace(input.CategoryCode) || string.IsNullOrWhiteSpace(input.Name)))
                throw Validation("New devices require categoryCode and name.");
            var category = device is null
                ? await FindByCodeAsync(unitOfWork.DeviceCategories, input.CategoryCode!, x => x.DisplayCode, "DEVICE_CATEGORY_NOT_FOUND", ct)
                : await unitOfWork.DeviceCategories.GetByIdAsync(device.CategoryId, ct)
                    ?? throw new UseCaseException(FailureKind.NotFound, "DEVICE_CATEGORY_NOT_FOUND", "The selected category was not found.");
            if (category.Status != EntityStatus.Active) throw Conflict("DEVICE_CATEGORY_UNAVAILABLE", "A selected category is locked.");
            var name = device?.Name ?? TextNormalization.Name(input.Name!);
            if (name.Length == 0) throw Validation("Device name is required.");
            var parentCode = device?.ParentDeviceId is { } parentId
                ? (await unitOfWork.Devices.GetByIdAsync(parentId, ct))?.DeviceCode : null;
            if (device is not null && (
                !string.IsNullOrWhiteSpace(input.CategoryCode) && !string.Equals(input.CategoryCode.Trim(), category.DisplayCode, StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrWhiteSpace(input.Name) && TextNormalization.Name(input.Name) != device.Name
                || !string.IsNullOrWhiteSpace(input.SerialNumber) && Clean(input.SerialNumber) != device.SerialNumber
                || !string.IsNullOrWhiteSpace(input.ParentDeviceCode) && !string.Equals(input.ParentDeviceCode.Trim(), parentCode, StringComparison.OrdinalIgnoreCase)))
                throw Validation("Existing device metadata must be edited on the device record before receipt.");
            var item = existing ?? new DeviceReceiptItem { DeviceCode = code, Barcode = device?.Barcode ?? BusinessCode.NewBarcode(), Name = name };
            item.CategoryId = category.Id;
            item.Name = name;
            item.DeviceCode = code;
            item.DeviceCodeSource = requestedCode is null ? DeviceCodeSource.Generated : existing?.DeviceCodeSource ?? DeviceCodeSource.Manual;
            item.SerialNumber = device?.SerialNumber ?? Clean(input.SerialNumber);
            if (!string.IsNullOrWhiteSpace(input.ParentDeviceCode) && !string.Equals(input.ParentDeviceCode.Trim(), existing?.ParentDeviceCode, StringComparison.OrdinalIgnoreCase))
                throw Conflict("DEVICE_COMPONENT_RELATIONSHIPS_RETIRED", "New receipt items cannot create component references.");
            item.ParentDeviceCode = existing?.ParentDeviceCode;
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

    private async Task ReserveIdentitiesAsync(IEnumerable<DeviceReceiptItem> items, CancellationToken ct)
    {
        foreach (var item in items)
            if (!await unitOfWork.DeviceIdentities.ExistsAsync(x => x.DeviceCode == item.DeviceCode, ct))
                await unitOfWork.DeviceIdentities.AddAsync(new DeviceIdentity
                    { DeviceCode = item.DeviceCode, Barcode = item.Barcode, ReservedAt = clock.GetUtcNow() }, ct);
    }

    private static void RequireReceivable(Device device, Guid? departmentId)
    {
        if (device.Quantity != 0) throw Conflict("DEVICE_ALREADY_RECEIVED", "The device has already been received.");
        if (device.Status != EntityStatus.Active) throw Conflict("DEVICE_LOCKED", "Unlock the device before receiving it.");
        if (departmentId is { } expectedDepartment && device.DepartmentId != expectedDepartment)
            throw Conflict("DEVICE_RECEIPT_DEPARTMENT_MISMATCH", "The device must belong to the receipt department.");
    }

    private async Task<Guid> ReceivingDepartmentAsync(Actor actor, DeviceReceipt? receipt, CancellationToken ct)
    {
        if (receipt?.WorkflowVersion == 1) return receipt.RequestingDepartmentId;
        var configured = await equipment.RequireConfiguredAsync(ct);
        if (receipt?.ReceivingDepartmentId is { } saved && saved != configured)
            throw Conflict("RECEIPT_RECEIVING_DEPARTMENT_CHANGED", "The receipt receiving department no longer matches Settings.");
        var account = await unitOfWork.Accounts.GetByIdAsync(actor.AccountId, ct) ?? throw Unauthorized();
        var profile = await unitOfWork.UserProfiles.GetByIdAsync(account.UserProfileId, ct) ?? throw Unauthorized();
        await equipment.RequireMemberAsync(actor.RoleCode, profile.DepartmentId, ct);
        return configured;
    }

    private async Task<Actor> RequireActorAsync(bool managerOnly, CancellationToken ct)
    {
        if (current.AccountId is not { } id || current.SessionId is not { } sessionId) throw Unauthorized();
        var account = await unitOfWork.Accounts.FirstOrDefaultAsync(x => x.Id == id && !x.IsLock, ct);
        var session = await unitOfWork.AuthSessions.FirstOrDefaultAsync(x => x.Id == sessionId && x.AccountId == id && x.IsActive && x.ExpiresAt > clock.GetUtcNow(), ct);
        if (account is null || session is null) throw Unauthorized();
        var role = await unitOfWork.Roles.GetByIdAsync(account.RoleId, ct);
        var roleCode = role?.Code ?? throw Forbidden("Receipt access is not permitted.");
        var permitted = roleCode is RoleCodes.EquipmentManager or RoleCodes.EquipmentStaff;
        if (!permitted || managerOnly && roleCode != RoleCodes.EquipmentManager) throw Forbidden("Receipt access is not permitted.");
        return new(id, roleCode);
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
            x.Barcode, x.DeviceCodeSource.ToString(), x.SerialNumber, x.Completed, x.ParentDeviceCode, x.ExistingDeviceCode)).ToArray() };
    }

    private static DeviceReceiptDto ToDto(DeviceReceiptReadModel x)
        => new(x.ReceiptCode, x.SupplierCode, x.SupplierName, x.CarrierName, x.RequestCode, x.DeliveryDate, x.DeliveryBatch,
            x.DepartmentCode, x.DepartmentName, x.PaidInvoiceCode, x.Status, x.CreatorAccountCode, x.CreatedAt,
            x.DecisionAccountCode, x.DecidedAt, x.CompletedAt, x.DecisionReason, x.Notes, x.ItemCount, x.Version)
        { WorkflowVersion = x.WorkflowVersion, ReceivingDepartmentCode = x.ReceivingDepartmentCode, ReceivingDepartmentName = x.ReceivingDepartmentName };

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

    private static string Snapshot(DeviceReceipt receipt, string supplierCode, string departmentCode, IEnumerable<DeviceReceiptItem> items, string? receivingDepartmentCode = null)
        => JsonSerializer.Serialize(new { receipt.ReceiptCode, receipt.WorkflowVersion, receivingDepartmentCode, supplierCode, receipt.CarrierName, receipt.RequestCode, receipt.DeliveryDate, receipt.DeliveryBatch,
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
