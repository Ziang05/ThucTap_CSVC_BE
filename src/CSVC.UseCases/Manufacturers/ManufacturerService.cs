using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AutoMapper;
using CSVC.Core.Accounts;
using CSVC.Core.Common;
using CSVC.Core.Manufacturers;
using CSVC.UseCases.Abstractions;
using CSVC.UseCases.Abstractions.Persistence;
using CSVC.UseCases.Abstractions.Security;
using CSVC.UseCases.Common;
using CSVC.UseCases.Manufacturers.DTOs;
using CSVC.UseCases.Exceptions;
using Microsoft.Extensions.Logging;

namespace CSVC.UseCases.Manufacturers;

public sealed class ManufacturerService(
    IUnitOfWork unitOfWork, IManufacturerTransaction transaction, ICurrentAccount current,
    IRequestContext requestContext, IMapper mapper, TimeProvider clock, ILogger<ManufacturerService> logger) : IManufacturerService
{
    private static readonly string[] SnapshotFields = ["display_code", "name", "description", "country", "website", "short_name", "contact_info", "notes", "status"];

    public Task<PagedResult<ManufacturerListItemDto>> ListAsync(ManufacturerListQuery query, CancellationToken ct)
        => ReadAsync(async () =>
        {
            await RequireActorAsync(false, ct);
            Validate(query);
            return await unitOfWork.Manufacturers.ListAsync(query, ct);
        });

    public Task<ManufacturerDetailDto> GetAsync(string displayCode, CancellationToken ct)
        => ReadAsync(async () =>
        {
            var actor = await RequireActorAsync(false, ct);
            var manufacturer = await FindAsync(displayCode, ct);
            var detail = mapper.Map<ManufacturerDetailDto>(manufacturer);
            if (IsEquipmentRole(actor.RoleCode))
            {
                detail.Version = Version(manufacturer);
                detail.Management = await unitOfWork.Manufacturers.GetManagementAsync(manufacturer.Id, ct);
            }
            else
            {
                detail.Notes = null;
                detail.Country = null;
                detail.Website = null;
                detail.ShortName = null;
                detail.ContactInfo = null;
            }
            return detail;
        });

    public Task<ManufacturerDetailDto> CreateAsync(CreateManufacturerRequest request, CancellationToken ct)
        => WriteAsync(null, "MANUFACTURER_CREATED", async token =>
        {
            var actor = await RequireActorAsync(true, token);
            Validate(request);
            await RequireUniqueNameAsync(request.Name, null, token);
            var manufacturer = mapper.Map<Manufacturer>(request);
            manufacturer.DisplayCode = request.DisplayCode ?? await GenerateCodeAsync(token);
            if (await unitOfWork.Manufacturers.DisplayCodeExistsAsync(manufacturer.DisplayCode, null, token))
                throw Error(FailureKind.Conflict, "MANUFACTURER_CODE_CONFLICT", "A manufacturer with this display code already exists.");
            manufacturer.Status = EntityStatus.Active;
            await unitOfWork.Manufacturers.AddAsync(manufacturer, token);
            await AuditAsync("MANUFACTURER_CREATED", manufacturer, actor.AccountId, null, Snapshot(manufacturer), AuditOutcome.Success, token);
            var detail = mapper.Map<ManufacturerDetailDto>(manufacturer);
            detail.Version = Version(manufacturer);
            var account = await unitOfWork.Accounts.GetByIdAsync(actor.AccountId, token);
            var profile = await unitOfWork.UserProfiles.GetByIdAsync(account!.UserProfileId, token);
            detail.Management = new()
            {
                CreatedByAccountCode = account.AccountCode, CreatedByFullName = profile?.FullName, CreatedAt = clock.GetUtcNow(),
                UpdatedByAccountCode = account.AccountCode, UpdatedByFullName = profile?.FullName, UpdatedAt = clock.GetUtcNow()
            };
            return detail;
        }, ct);

    public async Task UpdateAsync(string displayCode, UpdateManufacturerRequest request, CancellationToken ct)
        => await WriteAsync(displayCode, "MANUFACTURER_UPDATED", async token =>
        {
            var actor = await RequireActorAsync(true, token);
            Validate(request);
            var manufacturer = await FindAsync(displayCode, token);
            if (manufacturer.Status != EntityStatus.Active)
                throw Error(FailureKind.Conflict, "MANUFACTURER_LOCKED", "Unlock this manufacturer before editing it.");
            if (request.Version is not null && !string.Equals(request.Version, Version(manufacturer), StringComparison.OrdinalIgnoreCase))
                throw Error(FailureKind.Conflict, "MANUFACTURER_VERSION_CONFLICT", "This manufacturer has changed. Reload it before editing.");
            await RequireUniqueNameAsync(request.Name, manufacturer.Id, token);
            var before = Snapshot(manufacturer);
            mapper.Map(request, manufacturer);
            var after = Snapshot(manufacturer);
            if (before == after)
                await AuditAsync("MANUFACTURER_UPDATED", manufacturer, actor.AccountId, null, null, AuditOutcome.NoChange, token);
            else
            {
                unitOfWork.Manufacturers.Update(manufacturer);
                await AuditAsync("MANUFACTURER_UPDATED", manufacturer, actor.AccountId, before, after, AuditOutcome.Success, token);
            }
            return true;
        }, ct);

    public async Task SetLockedAsync(string displayCode, bool locked, CancellationToken ct)
        => await WriteAsync(displayCode, locked ? "MANUFACTURER_LOCKED" : "MANUFACTURER_UNLOCKED", async token =>
        {
            var actor = await RequireActorAsync(true, token);
            var manufacturer = await FindAsync(displayCode, token);
            var status = locked ? EntityStatus.Locked : EntityStatus.Active;
            var action = locked ? "MANUFACTURER_LOCKED" : "MANUFACTURER_UNLOCKED";
            if (manufacturer.Status == status)
                await AuditAsync(action, manufacturer, actor.AccountId, null, null, AuditOutcome.NoChange, token);
            else
            {
                var before = Snapshot(manufacturer);
                manufacturer.Status = status;
                unitOfWork.Manufacturers.Update(manufacturer);
                await AuditAsync(action, manufacturer, actor.AccountId, before, Snapshot(manufacturer), AuditOutcome.Success, token);
            }
            return true;
        }, ct);

    public Task<ManufacturerLockImpactDto> GetLockImpactAsync(string displayCode, CancellationToken ct)
        => ReadAsync(async () =>
        {
            await RequireActorAsync(true, ct);
            var manufacturer = await FindAsync(displayCode, ct);
            return new ManufacturerLockImpactDto
            {
                DisplayCode = manufacturer.DisplayCode, Status = manufacturer.Status,
                DeviceCount = await unitOfWork.Devices.CountByManufacturerAsync(manufacturer.Id, ct)
            };
        });

    public async Task DenyDeleteAsync(string displayCode, CancellationToken ct)
        => await WriteAsync<bool>(displayCode, "DELETE_DENIED", async token =>
        {
            await RequireActorAsync(true, token);
            await FindAsync(displayCode, token);
            throw Error(FailureKind.Forbidden, "MANUFACTURER_DELETE_FORBIDDEN", "Manufacturers cannot be deleted. Use lock instead.");
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

    public Task<PagedResult<ManufacturerHistoryListItemDto>> ListHistoryAsync(ManufacturerHistoryQuery query, CancellationToken ct, string? scope = null)
        => ReadAsync(async () =>
        {
            await RequireActorAsync(true, ct);
            Validate(query);
            var manufacturerId = scope is null ? (Guid?)null : (await FindAsync(scope, ct)).Id;
            return await unitOfWork.ManufacturerHistories.ListAsync(query, ct, manufacturerId);
        });

    public Task<ManufacturerHistoryDetailDto> GetHistoryAsync(Guid historyId, CancellationToken ct, string? scope = null)
        => ReadAsync(async () =>
        {
            var actor = await RequireActorAsync(true, ct);
            var manufacturerId = scope is null ? (Guid?)null : (await FindAsync(scope, ct)).Id;
            var item = await unitOfWork.ManufacturerHistories.GetDetailAsync(historyId, ct, manufacturerId)
                ?? throw Error(FailureKind.NotFound, "MANUFACTURER_HISTORY_NOT_FOUND", "Manufacturer history was not found.");
            var detail = mapper.Map<ManufacturerHistoryDetailDto>(item);
            detail.RequestId = actor.RoleCode == RoleCodes.EquipmentManager ? item.RequestId : null;
            // Do not echo arbitrary values from imported or older audit records.
            detail.ReasonCode = item.ReasonCode is "VALIDATION_FAILED" or "AUTHENTICATION_REQUIRED" or "MANUFACTURER_ACCESS_FORBIDDEN"
                or "MANUFACTURER_DELETE_FORBIDDEN" or "MANUFACTURER_NAME_CONFLICT" or "MANUFACTURER_CODE_CONFLICT" or "MANUFACTURER_NOT_FOUND"
                or "MANUFACTURER_LOCKED" or "MANUFACTURER_VERSION_CONFLICT" or "MANUFACTURER_UNAVAILABLE" ? item.ReasonCode : null;
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
            throw Error(FailureKind.Forbidden, "MANUFACTURER_ACCESS_FORBIDDEN", "Manufacturer access is not permitted.");
        return new(id, sessionId, role.Code);
    }

    private async Task<Manufacturer> FindAsync(string displayCode, CancellationToken ct)
        => await unitOfWork.Manufacturers.GetByDisplayCodeAsync(NormalizeCode(displayCode), ct)
            ?? throw Error(FailureKind.NotFound, "MANUFACTURER_NOT_FOUND", "Manufacturer was not found.");

    private async Task RequireUniqueNameAsync(string name, Guid? id, CancellationToken ct)
    {
        if (await unitOfWork.Manufacturers.NameExistsAsync(name, id, ct))
            throw Error(FailureKind.Conflict, "MANUFACTURER_NAME_CONFLICT", "A manufacturer with this name already exists.");
    }

    private async Task<string> GenerateCodeAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 32; attempt++)
        {
            var code = "HSX" + Guid.NewGuid().ToString("N").ToUpperInvariant();
            if (!await unitOfWork.Manufacturers.DisplayCodeExistsAsync(code, null, ct)) return code;
        }
        throw Unavailable();
    }

    private Task AuditAsync(string action, Manufacturer manufacturer, Guid actor, string? before, string? after, AuditOutcome outcome, CancellationToken ct)
    {
        var snapshot = Snapshot(manufacturer);
        return unitOfWork.ManufacturerHistories.AddAsync(new ManufacturerHistory
        {
            RequestId = requestContext.RequestId, ActorAccountId = actor, ManufacturerId = manufacturer.Id,
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
            await SaveFailureAsync(code, action, AuditOutcome.Failed, "MANUFACTURER_UNAVAILABLE", ct);
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
                Manufacturer? manufacturer = null;
                if (code is not null)
                {
                    try { manufacturer = await unitOfWork.Manufacturers.GetByDisplayCodeAsync(NormalizeCode(code), token); }
                    catch (UseCaseException) { /* Invalid route values are never copied into snapshots. */ }
                }
                await unitOfWork.ManufacturerHistories.AddAsync(new ManufacturerHistory
                {
                    RequestId = requestContext.RequestId, ActorAccountId = actor, ManufacturerId = manufacturer?.Id,
                    ActionCode = action, Outcome = outcome, ReasonCode = reason, OccurredAt = clock.GetUtcNow(),
                    BeforeData = manufacturer is null ? null : Snapshot(manufacturer)
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
        "Manufacturer persistence failed. RequestId: {RequestId}, ErrorType: {ErrorType}", requestContext.RequestId, exception.GetType().Name);

    private static string NormalizeCode(string value)
    {
        var code = BusinessCode.Normalize(Uri.UnescapeDataString(value));
        if (code is null || code.Length > 64 || !Regex.IsMatch(code, BusinessCode.Pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            throw Error(FailureKind.Validation, "VALIDATION_FAILED", "Manufacturer display code is invalid.");
        return code;
    }

    private static string Snapshot(Manufacturer manufacturer) => JsonSerializer.Serialize(new
    {
        display_code = manufacturer.DisplayCode, name = manufacturer.Name, description = manufacturer.Description,
        country = manufacturer.Country, website = manufacturer.Website, short_name = manufacturer.ShortName, contact_info = manufacturer.ContactInfo,
        notes = manufacturer.Notes, status = manufacturer.Status.ToString()
    });

    private static string Version(Manufacturer manufacturer)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Snapshot(manufacturer))));

    private static IReadOnlyDictionary<string, JsonElement>? SafeSnapshot(string? json)
    {
        if (json is null) return null;
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
        var result = new Dictionary<string, JsonElement>();
        foreach (var field in SnapshotFields)
            if (document.RootElement.TryGetProperty(field, out var value)
                && (value.ValueKind == JsonValueKind.Null || value.ValueKind == JsonValueKind.String
                    && value.GetString()!.Length <= SnapshotLimit(field)
                    && (field != "website" || ManufacturerWriteRequest.IsValidWebsite(value.GetString()!))))
                result[field] = value.Clone();
        return result;
    }

    private static int SnapshotLimit(string field) => field switch
    {
        "description" or "notes" or "contact_info" => 4000,
        "country" or "short_name" => 120, "website" => 2048, "name" => 200, _ => 64
    };

    private static string[] Changes(IReadOnlyDictionary<string, JsonElement> before, IReadOnlyDictionary<string, JsonElement> after)
        => SnapshotFields.Where(field => before.GetValueOrDefault(field).ValueKind != after.GetValueOrDefault(field).ValueKind
            || before.GetValueOrDefault(field).ToString() != after.GetValueOrDefault(field).ToString()).ToArray();

    private static bool IsEquipmentRole(string role) => role is RoleCodes.EquipmentManager or RoleCodes.EquipmentStaff;
    private static void Validate(object value)
    {
        if (!Validator.TryValidateObject(value, new ValidationContext(value), [], true))
            throw Error(FailureKind.Validation, "VALIDATION_FAILED", "Manufacturer request is invalid.");
    }
    private static UseCaseException AuthenticationRequired() => Error(FailureKind.Unauthorized, "AUTHENTICATION_REQUIRED", "A valid session is required.");
    private static UseCaseException Unavailable() => Error(FailureKind.Unavailable, "MANUFACTURER_UNAVAILABLE", "Manufacturers are temporarily unavailable.");
    private static UseCaseException Error(FailureKind kind, string code, string message) => new(kind, code, message);
}
