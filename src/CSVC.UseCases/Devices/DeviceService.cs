using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CSVC.Core.Accounts;
using CSVC.Core.Areas;
using CSVC.Core.Common;
using CSVC.Core.Departments;
using CSVC.Core.DeviceCategories;
using CSVC.Core.Devices;
using CSVC.Core.Manufacturers;
using CSVC.UseCases.Abstractions;
using CSVC.UseCases.Abstractions.Persistence;
using CSVC.UseCases.Abstractions.Security;
using CSVC.UseCases.Common;
using CSVC.UseCases.Devices.DTOs;
using CSVC.UseCases.Devices.Models;
using CSVC.UseCases.Exceptions;

namespace CSVC.UseCases.Devices;

public sealed class DeviceService(
    IUnitOfWork unitOfWork, IInventoryTransaction transaction, ICurrentAccount current,
    IRequestContext requestContext, TimeProvider clock) : IDeviceService
{
    private static readonly Regex DeviceCodePattern = new("^[A-Z0-9][A-Z0-9._/-]{0,31}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex BarcodePattern = new("^BC[A-F0-9]{30}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public async Task<PagedResult<DeviceDto>> ListAsync(DeviceListQuery query, CancellationToken ct)
    {
        var actor = await RequireActorAsync(false, ct);
        Validate(query);
        var page = await unitOfWork.Devices.ListAsync(query, actor.RoleCode == RoleCodes.DepartmentStaff ? actor.DepartmentId : null, ct);
        return new(page.Items.Select(x => ToDto(x, actor.RoleCode != RoleCodes.DepartmentStaff)).ToArray(), page.TotalCount, page.Page, page.PageSize);
    }

    public async Task<DeviceDto> GetAsync(string deviceCode, CancellationToken ct)
    {
        var actor = await RequireActorAsync(false, ct);
        var result = await unitOfWork.Devices.GetByCodeAsync(ValidateDeviceCode(deviceCode), actor.RoleCode == RoleCodes.DepartmentStaff ? actor.DepartmentId : null, ct)
            ?? throw Missing();
        return ToDto(result, true);
    }

    public async Task<DeviceDto> GetByBarcodeAsync(string barcode, CancellationToken ct)
    {
        var actor = await RequireActorAsync(false, ct);
        var value = barcode.Trim().ToUpperInvariant();
        if (!BarcodePattern.IsMatch(value)) throw Validation("Barcode format is invalid.");
        var result = await unitOfWork.Devices.GetByBarcodeAsync(value, actor.RoleCode == RoleCodes.DepartmentStaff ? actor.DepartmentId : null, ct)
            ?? throw Missing();
        return ToDto(result, true);
    }

    public async Task<DeviceDto> CreateAsync(CreateDeviceRequest request, CancellationToken ct)
        => await transaction.ExecuteAsync(async token =>
        {
            var actor = await RequireActorAsync(true, token);
            Validate(request);
            var name = TextNormalization.Name(request.Name);
            if (name.Length == 0) throw Validation("Name is required.");
            var category = await FindByCodeAsync(unitOfWork.DeviceCategories, request.CategoryCode, x => x.DisplayCode, "DEVICE_CATEGORY_NOT_FOUND", token);
            var department = await FindByCodeAsync(unitOfWork.Departments, request.DepartmentCode, x => x.Code, "DEPARTMENT_NOT_FOUND", token);
            if (category.Status != EntityStatus.Active) throw Conflict("DEVICE_CATEGORY_UNAVAILABLE", "The selected category is locked.");
            if (department.Status != EntityStatus.Active) throw Conflict("DEPARTMENT_UNAVAILABLE", "The selected department is locked.");
            var manufacturer = string.IsNullOrWhiteSpace(request.ManufacturerCode) ? null
                : await FindByCodeAsync(unitOfWork.Manufacturers, request.ManufacturerCode, x => x.DisplayCode, "MANUFACTURER_NOT_FOUND", token);
            if (manufacturer?.Status == EntityStatus.Locked) throw Conflict("MANUFACTURER_UNAVAILABLE", "The selected manufacturer is locked.");
            var area = string.IsNullOrWhiteSpace(request.AreaCode) ? null
                : await FindByCodeAsync(unitOfWork.Areas, request.AreaCode, x => x.Code, "AREA_NOT_FOUND", token);
            if (area?.Status == EntityStatus.Locked) throw Conflict("AREA_UNAVAILABLE", "The selected area is locked.");
            Account? responsible = null;
            if (!string.IsNullOrWhiteSpace(request.ResponsibleAccountCode))
            {
                responsible = await FindByCodeAsync(unitOfWork.Accounts, request.ResponsibleAccountCode, x => x.AccountCode, "RESPONSIBLE_ACCOUNT_NOT_FOUND", token);
                if (responsible.IsLock) throw Conflict("RESPONSIBLE_ACCOUNT_UNAVAILABLE", "The selected account is locked.");
                var profile = await unitOfWork.UserProfiles.GetByIdAsync(responsible.UserProfileId, token);
                if (profile?.DepartmentId != department.Id) throw Validation("Responsible account must belong to the selected department.");
            }

            var code = string.IsNullOrWhiteSpace(request.DeviceCode) ? "TB" + Guid.NewGuid().ToString("N").ToUpperInvariant() : NormalizeCode(request.DeviceCode);
            if (!DeviceCodePattern.IsMatch(code)) throw Validation("Device code format is invalid.");
            var barcode = "BC" + Guid.NewGuid().ToString("N").ToUpperInvariant();
            var identity = new DeviceIdentity { DeviceCode = code, Barcode = barcode, ReservedAt = clock.GetUtcNow() };
            var device = new Device
            {
                DeviceCode = code, Barcode = barcode, Name = name, CategoryId = category.Id,
                ManufacturerId = manufacturer?.Id, Model = Clean(request.Model), SerialNumber = Clean(request.SerialNumber),
                PurchaseDate = request.PurchaseDate, PaidInvoiceCode = Clean(request.PaidInvoiceCode), PurchasePrice = request.PurchasePrice,
                WarrantyExpiryDate = request.WarrantyExpiryDate, DepartmentId = department.Id, AreaId = area?.Id,
                ResponsibleAccountId = responsible?.Id, Condition = request.Condition, Status = EntityStatus.Active, Notes = Clean(request.Notes)
            };
            await unitOfWork.DeviceIdentities.AddAsync(identity, token);
            await unitOfWork.Devices.AddAsync(device, token);
            await AuditAsync(device, actor.AccountId, "DEVICE_CREATED", ["deviceCode", "barcode", "name", "categoryCode", "departmentCode", "responsibleAccountCode", "condition", "serialNumber", "manufacturerCode", "model", "purchaseDate", "paidInvoiceCode", "purchasePrice", "warrantyExpiryDate", "areaCode", "notes"], null,
                CreateSnapshot(device, category.DisplayCode, department.Code, responsible?.AccountCode, manufacturer?.DisplayCode, area?.Code), token);
            await unitOfWork.CommitAsync(token);
            var read = await unitOfWork.Devices.GetByCodeAsync(code, null, token) ?? throw Missing();
            return ToDto(read, true);
        }, ct);

    public async Task UpdateAsync(string deviceCode, UpdateDeviceRequest request, CancellationToken ct)
        => await transaction.ExecuteAsync(async token =>
        {
            var actor = await RequireActorAsync(true, token);
            Validate(request);
            var key = ValidateDeviceCode(deviceCode);
            var device = await unitOfWork.Devices.FirstOrDefaultTrackedAsync(x => x.DeviceCode == key, token) ?? throw Missing();
            if (device.Status == EntityStatus.Locked) throw Conflict("DEVICE_LOCKED", "Unlock the device before editing it.");
            var currentManufacturer = device.ManufacturerId is { } manufacturerId ? await unitOfWork.Manufacturers.GetByIdAsync(manufacturerId, token) : null;
            if (request.Version is not null && !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(request.Version.ToLowerInvariant()), Encoding.ASCII.GetBytes(VersionOf(device, currentManufacturer?.DisplayCode))))
                throw Conflict("VERSION_CONFLICT", "The device has changed. Reload and retry.");
            var before = EditableSnapshot(device, currentManufacturer?.DisplayCode);
            var newName = TextNormalization.Name(request.Name);
            if (newName.Length == 0) throw Validation("Name is required.");
            Manufacturer? manufacturer = string.IsNullOrWhiteSpace(request.ManufacturerCode) ? null
                : await FindByCodeAsync(unitOfWork.Manufacturers, request.ManufacturerCode, x => x.DisplayCode, "MANUFACTURER_NOT_FOUND", token);
            if (manufacturer?.Status == EntityStatus.Locked) throw Conflict("MANUFACTURER_UNAVAILABLE", "The selected manufacturer is locked.");
            device.Name = newName;
            device.SerialNumber = Clean(request.SerialNumber);
            device.ManufacturerId = manufacturer?.Id;
            device.Model = Clean(request.Model);
            device.PurchaseDate = request.PurchaseDate;
            device.PaidInvoiceCode = Clean(request.PaidInvoiceCode);
            device.PurchasePrice = request.PurchasePrice;
            device.WarrantyExpiryDate = request.WarrantyExpiryDate;
            device.Notes = Clean(request.Notes);
            var after = EditableSnapshot(device, manufacturer?.DisplayCode);
            var fields = ChangedFields(before, after);
            if (fields.Count == 0) return true;
            unitOfWork.Devices.Update(device);
            await AuditAsync(device, actor.AccountId, "DEVICE_UPDATED", fields, before, after, token);
            await unitOfWork.CommitAsync(token);
            return true;
        }, ct);

    public async Task SetParentAsync(string deviceCode, string? parentDeviceCode, CancellationToken ct)
        => await transaction.ExecuteAsync(async token =>
        {
            var actor = await RequireActorAsync(true, token);
            var code = ValidateDeviceCode(deviceCode);
            var device = await unitOfWork.Devices.FirstOrDefaultTrackedAsync(x => x.DeviceCode == code, token) ?? throw Missing();
            var before = device.ParentDeviceId is { } previousId ? await unitOfWork.Devices.GetByIdAsync(previousId, token) : null;
            Device? parent = string.IsNullOrWhiteSpace(parentDeviceCode) ? null : await unitOfWork.Devices.FirstOrDefaultAsync(x => x.DeviceCode == ValidateDeviceCode(parentDeviceCode), token);
            if (!string.IsNullOrWhiteSpace(parentDeviceCode) && parent is null) throw new UseCaseException(FailureKind.NotFound, "PARENT_DEVICE_NOT_FOUND", "Parent device was not found.");
            if (parent?.Id == device.Id || parent is not null && await unitOfWork.Devices.WouldCreateParentCycleAsync(device.Id, parent.Id, token))
                throw Conflict("INVALID_DEVICE_PARENT", "The selected parent would create a cycle.");
            if (parent is not null && (parent.DepartmentId != device.DepartmentId)) throw Conflict("DEVICE_PARENT_DEPARTMENT_MISMATCH", "Parent and child must belong to the same department when linked.");
            if (parent?.Status == EntityStatus.Locked) throw Conflict("DEVICE_LOCKED", "The selected parent device is locked.");
            var subtree = await unitOfWork.Devices.GetSubtreeAsync(device.Id, token);
            await transaction.LockDevicesAsync(subtree.Select(x => x.Id).Append(parent?.Id ?? Guid.Empty).Where(x => x != Guid.Empty), token);
            foreach (var node in subtree)
                if (await unitOfWork.DeviceOperationHolds.ExistsForDeviceAsync(node.Id, token) || await unitOfWork.Devices.HasPendingWorkflowAsync(node.Id, token)) throw Conflict("DEVICE_BUSY", "A device in this subtree has an active operation.");
            if (parent is not null && await unitOfWork.DeviceOperationHolds.ExistsForDeviceAsync(parent.Id, token)) throw Conflict("DEVICE_BUSY", "The selected parent device has an active operation.");
            if (parent is not null && await unitOfWork.Devices.HasPendingWorkflowAsync(parent.Id, token)) throw Conflict("DEVICE_BUSY", "The selected parent device has a pending operation.");
            device.ParentDeviceId = parent?.Id;
            unitOfWork.Devices.Update(device);
            await AuditAsync(device, actor.AccountId, parent is null ? "DEVICE_PARENT_UNLINKED" : before is null ? "DEVICE_PARENT_LINKED" : "DEVICE_PARENT_CHANGED",
                ["parentDeviceCode"], JsonSerializer.Serialize(new { parentDeviceCode = before?.DeviceCode }), JsonSerializer.Serialize(new { parentDeviceCode = parent?.DeviceCode }), token);
            await unitOfWork.CommitAsync(token);
            return true;
        }, ct);

    public async Task SetLockedAsync(string deviceCode, bool locked, CancellationToken ct)
        => await transaction.ExecuteAsync(async token =>
        {
            var actor = await RequireActorAsync(true, token);
            var key = ValidateDeviceCode(deviceCode);
            var device = await unitOfWork.Devices.FirstOrDefaultTrackedAsync(x => x.DeviceCode == key, token) ?? throw Missing();
            var next = locked ? EntityStatus.Locked : EntityStatus.Active;
            var subtree = await unitOfWork.Devices.GetSubtreeAsync(device.Id, token);
            await transaction.LockDevicesAsync(subtree.Select(x => x.Id), token);
            foreach (var item in subtree)
            {
                var affected = item.Id == device.Id ? device : await unitOfWork.Devices.FirstOrDefaultTrackedAsync(x => x.Id == item.Id, token) ?? throw Missing();
                if (locked && await unitOfWork.DeviceOperationHolds.ExistsForDeviceAsync(affected.Id, token))
                    throw Conflict("DEVICE_BUSY", "A device in this subtree has an active operation.");
                if (affected.Status == next) continue;
                var before = StateSnapshot(affected);
                affected.Status = next;
                unitOfWork.Devices.Update(affected);
                await AuditAsync(affected, actor.AccountId, (locked ? "DEVICE_LOCKED_" : "DEVICE_UNLOCKED_") + affected.DeviceCode, ["status"], before, StateSnapshot(affected), token);
            }
            await unitOfWork.CommitAsync(token);
            return true;
        }, ct);

    public async Task<PagedResult<DeviceHistoryDto>> HistoryAsync(string deviceCode, DeviceHistoryQuery query, CancellationToken ct)
    {
        var actor = await RequireActorAsync(false, ct);
        Validate(query);
        var device = await unitOfWork.Devices.GetByCodeAsync(ValidateDeviceCode(deviceCode), actor.RoleCode == RoleCodes.DepartmentStaff ? actor.DepartmentId : null, ct)
            ?? throw Missing();
        var page = await unitOfWork.DeviceHistories.ListTimelineForDeviceAsync(device.Id, query, ct);
        return new(page.Items.Select(x => ToHistoryDto(x, actor.RoleCode != RoleCodes.DepartmentStaff)).ToArray(),
            page.TotalCount, page.Page, page.PageSize);
    }

    public async Task<PagedResult<DeviceHistoryDto>> GlobalHistoryAsync(DeviceHistoryQuery query, CancellationToken ct)
    {
        var actor = await RequireActorAsync(true, ct);
        Validate(query);
        if (query.FromDate > query.ToDate) throw Validation("fromDate cannot be later than toDate.");
        var page = await unitOfWork.DeviceHistories.ListGlobalTimelineAsync(query, ct);
        return new(page.Items.Select(x => ToHistoryDto(x, true)).ToArray(), page.TotalCount, page.Page, page.PageSize);
    }

    private static DeviceHistoryDto ToHistoryDto(DeviceHistoryReadModel x, bool includeAudit)
        => new(x.ActionCode, x.Outcome.ToString(), x.OccurredAt, x.ActorAccountCode, x.ActorFullName, x.ReasonCode,
            includeAudit ? x.ChangedFields : null, includeAudit ? x.BeforeData : null, includeAudit ? x.AfterData : null,
            x.EventType, x.BusinessCode, x.DeviceCode, x.DeviceName, x.DepartmentCode);

    private async Task<Actor> RequireActorAsync(bool management, CancellationToken ct)
    {
        if (current.AccountId is not { } id || current.SessionId is not { } sessionId)
            throw new UseCaseException(FailureKind.Unauthorized, "AUTHENTICATION_REQUIRED", "A valid session is required.");
        var account = await unitOfWork.Accounts.FirstOrDefaultAsync(x => x.Id == id && !x.IsLock, ct);
        var session = await unitOfWork.AuthSessions.FirstOrDefaultAsync(x => x.Id == sessionId && x.AccountId == id && x.IsActive && x.ExpiresAt > clock.GetUtcNow(), ct);
        if (account is null || session is null) throw new UseCaseException(FailureKind.Unauthorized, "AUTHENTICATION_REQUIRED", "A valid session is required.");
        var role = await unitOfWork.Roles.GetByIdAsync(account.RoleId, ct);
        var allowed = role?.Code is RoleCodes.EquipmentManager or RoleCodes.EquipmentStaff or RoleCodes.DepartmentStaff;
        if (!allowed || management && role!.Code == RoleCodes.DepartmentStaff)
            throw new UseCaseException(FailureKind.Forbidden, "DEVICE_ACCESS_FORBIDDEN", "Device access is not permitted.");
        var profile = await unitOfWork.UserProfiles.GetByIdAsync(account.UserProfileId, ct);
        if (profile is null) throw new UseCaseException(FailureKind.Unauthorized, "AUTHENTICATION_REQUIRED", "A valid account profile is required.");
        return new(id, role!.Code, profile.DepartmentId);
    }

    private async Task<T> FindByCodeAsync<T>(IRepository<T> repository, string code, System.Linq.Expressions.Expression<Func<T, string>> selector, string errorCode, CancellationToken ct) where T : class
    {
        var value = code.Trim().ToLowerInvariant();
        var parameter = selector.Parameters[0];
        var body = System.Linq.Expressions.Expression.Equal(System.Linq.Expressions.Expression.Call(selector.Body, typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!), System.Linq.Expressions.Expression.Constant(value));
        var predicate = System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(body, parameter);
        return await repository.FirstOrDefaultAsync(predicate, ct) ?? throw new UseCaseException(FailureKind.NotFound, errorCode, "The selected related record was not found.");
    }

    private Task AuditAsync(Device device, Guid actor, string action, IReadOnlyList<string> fields, string? before, string after, CancellationToken ct)
        => unitOfWork.DeviceHistories.AddAsync(new DeviceHistory
        {
            RequestId = requestContext.RequestId, ActorAccountId = actor, DeviceId = device.Id, ActionCode = action,
            Outcome = AuditOutcome.Success, OccurredAt = clock.GetUtcNow(), ChangedFields = JsonSerializer.Serialize(fields),
            BeforeData = before, AfterData = after
        }, ct);

    private static DeviceDto ToDto(DeviceReadModel x, bool includeBarcode)
        => new(x.DeviceCode, includeBarcode ? x.Barcode : null, x.Name, x.CategoryCode, x.CategoryName, x.DepartmentCode, x.DepartmentName,
            x.AreaCode, x.Status, x.Condition, Availability(x), x.ResponsibleAccountCode, x.ResponsibleFullName,
            x.ManufacturerCode, x.ManufacturerName, x.Model, x.SerialNumber, x.PurchaseDate, x.PaidInvoiceCode,
            x.PurchasePrice, x.WarrantyExpiryDate, x.Notes, VersionOf(x), x.ParentDeviceCode, x.ParentDeviceName, x.IsComponent);

    private static DeviceAvailability Availability(DeviceReadModel x) => !x.HasHold ? DeviceAvailability.Available
        : x.IsOnLoan ? DeviceAvailability.OnLoan
        : x.HoldKind == DeviceOperationKind.Transfer ? DeviceAvailability.TransferPending : DeviceAvailability.Reserved;

    private static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();
    private static string ValidateDeviceCode(string code)
    {
        var value = NormalizeCode(code);
        if (!DeviceCodePattern.IsMatch(value)) throw Validation("Device code format is invalid.");
        return value;
    }
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : TextNormalization.Name(value);
    private static string VersionOf(Device device, string? manufacturerCode) => Hash(new { device.Name, device.SerialNumber, ManufacturerCode = manufacturerCode, device.Model, device.PurchaseDate, device.PaidInvoiceCode, device.PurchasePrice, device.WarrantyExpiryDate, device.Notes });
    private static string VersionOf(DeviceReadModel x) => Hash(new { x.Name, x.SerialNumber, x.ManufacturerCode, x.Model, x.PurchaseDate, x.PaidInvoiceCode, x.PurchasePrice, x.WarrantyExpiryDate, x.Notes });
    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)))).ToLowerInvariant();
    private static string EditableSnapshot(Device d, string? manufacturerCode) => JsonSerializer.Serialize(new { d.Name, d.SerialNumber, ManufacturerCode = manufacturerCode, d.Model, d.PurchaseDate, d.PaidInvoiceCode, d.PurchasePrice, d.WarrantyExpiryDate, d.Notes });
    private static string StateSnapshot(Device d) => JsonSerializer.Serialize(new { d.DeviceCode, status = d.Status.ToString() });
    private static string CreateSnapshot(Device d, string categoryCode, string departmentCode, string? responsibleAccountCode, string? manufacturerCode, string? areaCode)
        => JsonSerializer.Serialize(new { d.DeviceCode, d.Barcode, d.Name, categoryCode, departmentCode, responsibleAccountCode, manufacturerCode, areaCode, condition = d.Condition.ToString(), d.SerialNumber, d.Model, d.PurchaseDate, d.PaidInvoiceCode, d.PurchasePrice, d.WarrantyExpiryDate, d.Notes });
    private static List<string> ChangedFields(string before, string after)
    {
        var a = JsonDocument.Parse(before).RootElement; var b = JsonDocument.Parse(after).RootElement; List<string> fields = [];
        foreach (var property in b.EnumerateObject()) if (a.GetProperty(property.Name).GetRawText() != property.Value.GetRawText()) fields.Add(char.ToLowerInvariant(property.Name[0]) + property.Name[1..]);
        return fields;
    }
    private static void Validate(object request)
    {
        if (!Validator.TryValidateObject(request, new ValidationContext(request), [], true)) throw Validation("Request validation failed.");
    }
    private static UseCaseException Missing() => new(FailureKind.NotFound, "DEVICE_NOT_FOUND", "Device was not found.");
    private static UseCaseException Validation(string message) => new(FailureKind.Validation, "VALIDATION_FAILED", message);
    private static UseCaseException Conflict(string code, string message) => new(FailureKind.Conflict, code, message);
    private sealed record Actor(Guid AccountId, string RoleCode, Guid DepartmentId);
}
