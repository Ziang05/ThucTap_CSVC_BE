using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AutoMapper;
using CSVC.Core.Accounts;
using CSVC.Core.Common;
using CSVC.Core.Suppliers;
using CSVC.UseCases.Abstractions;
using CSVC.UseCases.Abstractions.Persistence;
using CSVC.UseCases.Abstractions.Security;
using CSVC.UseCases.Common;
using CSVC.UseCases.Suppliers.DTOs;
using CSVC.UseCases.Exceptions;
using Microsoft.Extensions.Logging;

namespace CSVC.UseCases.Suppliers;

public sealed class SupplierService(
    IUnitOfWork unitOfWork, ISupplierTransaction transaction, ICurrentAccount current,
    IRequestContext requestContext, IMapper mapper, TimeProvider clock, ILogger<SupplierService> logger) : ISupplierService
{
    private static readonly string[] SnapshotFields = ["display_code", "name", "description", "tax_code", "address", "phone", "email", "notes", "status"];

    public Task<PagedResult<SupplierListItemDto>> ListAsync(SupplierListQuery query, CancellationToken ct)
        => ReadAsync(async () =>
        {
            await RequireActorAsync(false, ct);
            Validate(query);
            return await unitOfWork.Suppliers.ListAsync(query, ct);
        });

    public Task<SupplierDetailDto> GetAsync(string displayCode, CancellationToken ct)
        => ReadAsync(async () =>
        {
            var actor = await RequireActorAsync(false, ct);
            var supplier = await FindAsync(displayCode, ct);
            var detail = mapper.Map<SupplierDetailDto>(supplier);
            if (IsEquipmentRole(actor.RoleCode))
            {
                detail.Version = Version(supplier);
                detail.Management = await unitOfWork.Suppliers.GetManagementAsync(supplier.Id, ct);
            }
            else
            {
                detail.Notes = null;
                detail.TaxCode = null;
                detail.Address = null;
                detail.Phone = null;
                detail.Email = null;
            }
            return detail;
        });

    public Task<SupplierDetailDto> CreateAsync(CreateSupplierRequest request, CancellationToken ct)
        => WriteAsync(null, "SUPPLIER_CREATED", async token =>
        {
            var actor = await RequireActorAsync(true, token);
            Validate(request);
            await RequireUniqueNameAsync(request.Name, null, token);
            await RequireUniqueTaxCodeAsync(request.TaxCode, null, token);
            var supplier = mapper.Map<Supplier>(request);
            supplier.DisplayCode = request.DisplayCode ?? await GenerateCodeAsync(token);
            if (await unitOfWork.Suppliers.DisplayCodeExistsAsync(supplier.DisplayCode, null, token))
                throw Error(FailureKind.Conflict, "SUPPLIER_CODE_CONFLICT", "A supplier with this display code already exists.");
            supplier.Status = EntityStatus.Active;
            await unitOfWork.Suppliers.AddAsync(supplier, token);
            await AuditAsync("SUPPLIER_CREATED", supplier, actor.AccountId, null, Snapshot(supplier), AuditOutcome.Success, token);
            var detail = mapper.Map<SupplierDetailDto>(supplier);
            detail.Version = Version(supplier);
            var account = await unitOfWork.Accounts.GetByIdAsync(actor.AccountId, token);
            var profile = await unitOfWork.UserProfiles.GetByIdAsync(account!.UserProfileId, token);
            detail.Management = new()
            {
                CreatedByAccountCode = account.AccountCode, CreatedByFullName = profile?.FullName, CreatedAt = clock.GetUtcNow(),
                UpdatedByAccountCode = account.AccountCode, UpdatedByFullName = profile?.FullName, UpdatedAt = clock.GetUtcNow()
            };
            return detail;
        }, ct);

    public async Task UpdateAsync(string displayCode, UpdateSupplierRequest request, CancellationToken ct)
        => await WriteAsync(displayCode, "SUPPLIER_UPDATED", async token =>
        {
            var actor = await RequireActorAsync(true, token);
            Validate(request);
            var supplier = await FindAsync(displayCode, token);
            if (supplier.Status != EntityStatus.Active)
                throw Error(FailureKind.Conflict, "SUPPLIER_LOCKED", "Unlock this supplier before editing it.");
            if (request.Version is not null && !string.Equals(request.Version, Version(supplier), StringComparison.OrdinalIgnoreCase))
                throw Error(FailureKind.Conflict, "SUPPLIER_VERSION_CONFLICT", "This supplier has changed. Reload it before editing.");
            await RequireUniqueNameAsync(request.Name, supplier.Id, token);
            await RequireUniqueTaxCodeAsync(request.TaxCode, supplier.Id, token);
            var before = Snapshot(supplier);
            mapper.Map(request, supplier);
            var after = Snapshot(supplier);
            if (before == after)
                await AuditAsync("SUPPLIER_UPDATED", supplier, actor.AccountId, null, null, AuditOutcome.NoChange, token);
            else
            {
                unitOfWork.Suppliers.Update(supplier);
                await AuditAsync("SUPPLIER_UPDATED", supplier, actor.AccountId, before, after, AuditOutcome.Success, token);
            }
            return true;
        }, ct);

    public async Task SetLockedAsync(string displayCode, bool locked, CancellationToken ct)
        => await WriteAsync(displayCode, locked ? "SUPPLIER_LOCKED" : "SUPPLIER_UNLOCKED", async token =>
        {
            var actor = await RequireActorAsync(true, token);
            var supplier = await FindAsync(displayCode, token);
            var status = locked ? EntityStatus.Locked : EntityStatus.Active;
            var action = locked ? "SUPPLIER_LOCKED" : "SUPPLIER_UNLOCKED";
            if (supplier.Status == status)
                await AuditAsync(action, supplier, actor.AccountId, null, null, AuditOutcome.NoChange, token);
            else
            {
                var before = Snapshot(supplier);
                supplier.Status = status;
                unitOfWork.Suppliers.Update(supplier);
                await AuditAsync(action, supplier, actor.AccountId, before, Snapshot(supplier), AuditOutcome.Success, token);
            }
            return true;
        }, ct);

    public Task<SupplierLockImpactDto> GetLockImpactAsync(string displayCode, CancellationToken ct)
        => ReadAsync(async () =>
        {
            await RequireActorAsync(true, ct);
            var supplier = await FindAsync(displayCode, ct);
            return new SupplierLockImpactDto
            {
                DisplayCode = supplier.DisplayCode, Status = supplier.Status,
                ReceiptCount = await unitOfWork.DeviceReceipts.CountForSupplierAsync(supplier.Id, ct)
            };
        });

    public async Task DenyDeleteAsync(string displayCode, CancellationToken ct)
        => await WriteAsync<bool>(displayCode, "DELETE_DENIED", async token =>
        {
            await RequireActorAsync(true, token);
            await FindAsync(displayCode, token);
            throw Error(FailureKind.Forbidden, "SUPPLIER_DELETE_FORBIDDEN", "Suppliers cannot be deleted. Use lock instead.");
        }, ct);

    public async Task RecordInvalidRequestAsync(string? displayCode, string action, CancellationToken ct)
    {
        try { await RequireActorAsync(true, ct); }
        catch (UseCaseException failure)
        {
            await SaveFailureAsync(displayCode, action, AuditOutcome.Denied, failure.Code, ct);
            throw;
        }
        await SaveFailureAsync(displayCode, action, AuditOutcome.Failed, "VALIDATION_FAILED", ct);
    }

    public Task<PagedResult<SupplierHistoryListItemDto>> ListHistoryAsync(SupplierHistoryQuery query, CancellationToken ct, string? scope = null)
        => ReadAsync(async () =>
        {
            await RequireActorAsync(true, ct);
            Validate(query);
            var supplierId = scope is null ? (Guid?)null : (await FindAsync(scope, ct)).Id;
            return await unitOfWork.SupplierHistories.ListAsync(query, ct, supplierId);
        });

    public Task<SupplierHistoryDetailDto> GetHistoryAsync(Guid historyId, CancellationToken ct, string? scope = null)
        => ReadAsync(async () =>
        {
            var actor = await RequireActorAsync(true, ct);
            var supplierId = scope is null ? (Guid?)null : (await FindAsync(scope, ct)).Id;
            var item = await unitOfWork.SupplierHistories.GetDetailAsync(historyId, ct, supplierId)
                ?? throw Error(FailureKind.NotFound, "SUPPLIER_HISTORY_NOT_FOUND", "Supplier history was not found.");
            var detail = mapper.Map<SupplierHistoryDetailDto>(item);
            detail.RequestId = actor.RoleCode == RoleCodes.EquipmentManager ? item.RequestId : null;
            // Do not echo arbitrary values from imported or older audit records.
            detail.ReasonCode = item.ReasonCode is "VALIDATION_FAILED" or "AUTHENTICATION_REQUIRED" or "SUPPLIER_ACCESS_FORBIDDEN"
                or "SUPPLIER_DELETE_FORBIDDEN" or "SUPPLIER_NAME_CONFLICT" or "SUPPLIER_CODE_CONFLICT" or "SUPPLIER_NOT_FOUND"
                or "SUPPLIER_TAX_CODE_CONFLICT" or "SUPPLIER_LOCKED" or "SUPPLIER_VERSION_CONFLICT" or "SUPPLIER_UNAVAILABLE" ? item.ReasonCode : null;
            detail.BeforeData = SafeSnapshot(item.BeforeData);
            detail.AfterData = SafeSnapshot(item.AfterData);
            detail.ChangedFields = item.Outcome == AuditOutcome.Success && detail.BeforeData is not null && detail.AfterData is not null
                ? Changes(detail.BeforeData, detail.AfterData) : null;
            return detail;
        });

    private async Task<SessionIdentity> RequireActorAsync(bool equipmentOnly, CancellationToken ct)
    {
        if (current.AccountId is not { } id || current.SessionId is not { } sessionId) throw AuthenticationRequired();
        var account = await unitOfWork.Accounts.FirstOrDefaultAsync(a => a.Id == id && !a.IsLock, ct);
        var session = await unitOfWork.AuthSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.AccountId == id
            && s.IsActive && s.ExpiresAt > clock.GetUtcNow(), ct);
        if (account is null || session is null) throw AuthenticationRequired();
        var role = await unitOfWork.Roles.GetByIdAsync(account.RoleId, ct);
        if (role is null || !IsEquipmentRole(role.Code) && (equipmentOnly || role.Code != RoleCodes.DepartmentStaff))
            throw Error(FailureKind.Forbidden, "SUPPLIER_ACCESS_FORBIDDEN", "Supplier access is not permitted.");
        return new(id, sessionId, role.Code);
    }

    private async Task<Supplier> FindAsync(string displayCode, CancellationToken ct)
        => await unitOfWork.Suppliers.GetByDisplayCodeAsync(NormalizeCode(displayCode), ct)
            ?? throw Error(FailureKind.NotFound, "SUPPLIER_NOT_FOUND", "Supplier was not found.");

    private async Task RequireUniqueNameAsync(string name, Guid? id, CancellationToken ct)
    {
        if (await unitOfWork.Suppliers.NameExistsAsync(name, id, ct))
            throw Error(FailureKind.Conflict, "SUPPLIER_NAME_CONFLICT", "A supplier with this name already exists.");
    }

    private async Task RequireUniqueTaxCodeAsync(string? taxCode, Guid? id, CancellationToken ct)
    {
        if (taxCode is not null && await unitOfWork.Suppliers.TaxCodeExistsAsync(taxCode, id, ct))
            throw Error(FailureKind.Conflict, "SUPPLIER_TAX_CODE_CONFLICT", "A supplier with this tax code already exists.");
    }

    private async Task<string> GenerateCodeAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 32; attempt++)
        {
            var code = "NCC" + Guid.NewGuid().ToString("N").ToUpperInvariant();
            if (!await unitOfWork.Suppliers.DisplayCodeExistsAsync(code, null, ct)) return code;
        }
        throw Unavailable();
    }

    private Task AuditAsync(string action, Supplier supplier, Guid actor, string? before, string? after, AuditOutcome outcome, CancellationToken ct)
    {
        var snapshot = Snapshot(supplier);
        return unitOfWork.SupplierHistories.AddAsync(new SupplierHistory
        {
            RequestId = requestContext.RequestId, ActorAccountId = actor, SupplierId = supplier.Id,
            ActionCode = action, Outcome = outcome, OccurredAt = clock.GetUtcNow(),
            BeforeData = before ?? (outcome == AuditOutcome.NoChange ? snapshot : null),
            AfterData = after ?? (outcome == AuditOutcome.NoChange ? snapshot : null),
            ChangedFields = before is not null && after is not null
                ? JsonSerializer.Serialize(Changes(SafeSnapshot(before)!, SafeSnapshot(after)!)) : null
        }, ct);
    }

    private async Task<T> WriteAsync<T>(string? code, string action, Func<CancellationToken, Task<T>> work, CancellationToken ct)
    {
        try { return await transaction.ExecuteAsync(work, ct); }
        catch (UseCaseException failure)
        {
            await SaveFailureAsync(code, action, failure.Kind is FailureKind.Forbidden or FailureKind.Unauthorized
                ? AuditOutcome.Denied : AuditOutcome.Failed, failure.Code, ct);
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailure(exception);
            await SaveFailureAsync(code, action, AuditOutcome.Failed, "SUPPLIER_UNAVAILABLE", ct);
            throw Unavailable();
        }
    }

    private async Task SaveFailureAsync(string? code, string action, AuditOutcome outcome, string reason, CancellationToken ct)
    {
        try
        {
            await transaction.ExecuteAsync(async token =>
            {
                var actor = current.AccountId is { } id && await unitOfWork.Accounts.ExistsAsync(a => a.Id == id, token) ? (Guid?)id : null;
                Supplier? supplier = null;
                if (code is not null)
                {
                    try { supplier = await unitOfWork.Suppliers.GetByDisplayCodeAsync(NormalizeCode(code), token); }
                    catch (UseCaseException) { /* Invalid route values are never copied into snapshots. */ }
                }
                await unitOfWork.SupplierHistories.AddAsync(new SupplierHistory
                {
                    RequestId = requestContext.RequestId, ActorAccountId = actor, SupplierId = supplier?.Id,
                    ActionCode = action, Outcome = outcome, ReasonCode = reason, OccurredAt = clock.GetUtcNow(),
                    BeforeData = supplier is null ? null : Snapshot(supplier)
                }, token);
                return true;
            }, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailure(exception);
            throw Unavailable();
        }
    }

    private async Task<T> ReadAsync<T>(Func<Task<T>> work)
    {
        try { return await work(); }
        catch (Exception exception) when (exception is not (UseCaseException or OperationCanceledException))
        {
            LogFailure(exception);
            throw Unavailable();
        }
    }

    private void LogFailure(Exception exception) => logger.LogError(
        "Supplier persistence failed. RequestId: {RequestId}, ErrorType: {ErrorType}", requestContext.RequestId, exception.GetType().Name);

    private static string NormalizeCode(string value)
    {
        var code = BusinessCode.Normalize(Uri.UnescapeDataString(value));
        if (code is null || code.Length > 64 || !Regex.IsMatch(code, BusinessCode.Pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            throw Error(FailureKind.Validation, "VALIDATION_FAILED", "Supplier display code is invalid.");
        return code;
    }

    private static string Snapshot(Supplier supplier) => JsonSerializer.Serialize(new
    {
        display_code = supplier.DisplayCode, name = supplier.Name, description = supplier.Description,
        tax_code = supplier.TaxCode, address = supplier.Address, phone = supplier.Phone, email = supplier.Email,
        notes = supplier.Notes, status = supplier.Status.ToString()
    });

    private static string Version(Supplier supplier)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Snapshot(supplier))));

    private static IReadOnlyDictionary<string, JsonElement>? SafeSnapshot(string? json)
    {
        if (json is null) return null;
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
        var result = new Dictionary<string, JsonElement>();
        foreach (var field in SnapshotFields)
            if (document.RootElement.TryGetProperty(field, out var value)
                && (value.ValueKind == JsonValueKind.Null || value.ValueKind == JsonValueKind.String
                    && value.GetString()!.Length <= SnapshotLimit(field)))
                result[field] = value.Clone();
        return result;
    }

    private static int SnapshotLimit(string field) => field switch
    {
        "description" or "notes" or "address" => 4000,
        "name" => 200, "email" => 320, "tax_code" => 50, "phone" => 32, _ => 64
    };

    private static string[] Changes(IReadOnlyDictionary<string, JsonElement> before, IReadOnlyDictionary<string, JsonElement> after)
        => SnapshotFields.Where(field => before.GetValueOrDefault(field).ToString() != after.GetValueOrDefault(field).ToString()).ToArray();

    private static bool IsEquipmentRole(string role) => role is RoleCodes.EquipmentManager or RoleCodes.EquipmentStaff;
    private static void Validate(object value)
    {
        if (!Validator.TryValidateObject(value, new ValidationContext(value), [], true))
            throw Error(FailureKind.Validation, "VALIDATION_FAILED", "Supplier request is invalid.");
    }
    private static UseCaseException AuthenticationRequired() => Error(FailureKind.Unauthorized, "AUTHENTICATION_REQUIRED", "A valid session is required.");
    private static UseCaseException Unavailable() => Error(FailureKind.Unavailable, "SUPPLIER_UNAVAILABLE", "Suppliers are temporarily unavailable.");
    private static UseCaseException Error(FailureKind kind, string code, string message) => new(kind, code, message);
}
