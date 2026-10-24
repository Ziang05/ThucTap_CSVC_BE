using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AutoMapper;
using CSVC.Core.Accounts;
using CSVC.Core.Common;
using CSVC.Core.DeviceCategories;
using CSVC.UseCases.Abstractions;
using CSVC.UseCases.Abstractions.Persistence;
using CSVC.UseCases.Abstractions.Security;
using CSVC.UseCases.Common;
using CSVC.UseCases.DeviceCategories.DTOs;
using CSVC.UseCases.Exceptions;
using Microsoft.Extensions.Logging;

namespace CSVC.UseCases.DeviceCategories;

public sealed class CategoryService(
    IUnitOfWork unitOfWork, ICategoryTransaction transaction, ICurrentAccount current,
    IRequestContext requestContext, IMapper mapper, TimeProvider clock, ILogger<CategoryService> logger) : ICategoryService
{
    private static readonly string[] SnapshotFields = ["display_code", "name", "description", "notes", "status"];

    public Task<PagedResult<CategoryListItemDto>> ListAsync(CategoryListQuery query, CancellationToken ct)
        => ReadAsync(async () =>
        {
            await RequireActorAsync(false, ct);
            Validate(query);
            return await unitOfWork.DeviceCategories.ListAsync(query, ct);
        });

    public Task<CategoryDetailDto> GetAsync(string displayCode, CancellationToken ct)
        => ReadAsync(async () =>
        {
            var actor = await RequireActorAsync(false, ct);
            var category = await FindAsync(displayCode, ct);
            var detail = mapper.Map<CategoryDetailDto>(category);
            if (IsEquipmentRole(actor.RoleCode))
            {
                detail.Version = Version(category);
                detail.Management = await unitOfWork.DeviceCategories.GetManagementAsync(category.Id, ct);
            }
            else detail.Notes = null;
            return detail;
        });

    public Task<CategoryDetailDto> CreateAsync(CreateCategoryRequest request, CancellationToken ct)
        => WriteAsync(null, "CATEGORY_CREATED", async token =>
        {
            var actor = await RequireActorAsync(true, token);
            Validate(request);
            await RequireUniqueNameAsync(request.Name, null, token);
            var category = mapper.Map<DeviceCategory>(request);
            category.DisplayCode = request.DisplayCode ?? await GenerateCodeAsync(token);
            if (await unitOfWork.DeviceCategories.DisplayCodeExistsAsync(category.DisplayCode, null, token))
                throw Error(FailureKind.Conflict, "CATEGORY_CODE_CONFLICT", "A category with this display code already exists.");
            category.Status = EntityStatus.Active;
            await unitOfWork.DeviceCategories.AddAsync(category, token);
            await AuditAsync("CATEGORY_CREATED", category, actor.AccountId, null, Snapshot(category), AuditOutcome.Success, token);
            var detail = mapper.Map<CategoryDetailDto>(category);
            detail.Version = Version(category);
            var account = await unitOfWork.Accounts.GetByIdAsync(actor.AccountId, token);
            var profile = await unitOfWork.UserProfiles.GetByIdAsync(account!.UserProfileId, token);
            detail.Management = new()
            {
                CreatedByAccountCode = account.AccountCode, CreatedByFullName = profile?.FullName, CreatedAt = clock.GetUtcNow(),
                UpdatedByAccountCode = account.AccountCode, UpdatedByFullName = profile?.FullName, UpdatedAt = clock.GetUtcNow()
            };
            return detail;
        }, ct);

    public async Task UpdateAsync(string displayCode, UpdateCategoryRequest request, CancellationToken ct)
        => await WriteAsync(displayCode, "CATEGORY_UPDATED", async token =>
        {
            var actor = await RequireActorAsync(true, token);
            Validate(request);
            var category = await FindAsync(displayCode, token);
            if (category.Status != EntityStatus.Active)
                throw Error(FailureKind.Conflict, "CATEGORY_LOCKED", "Unlock this category before editing it.");
            if (request.Version is not null && !string.Equals(request.Version, Version(category), StringComparison.OrdinalIgnoreCase))
                throw Error(FailureKind.Conflict, "CATEGORY_VERSION_CONFLICT", "This category has changed. Reload it before editing.");
            await RequireUniqueNameAsync(request.Name, category.Id, token);
            var before = Snapshot(category);
            mapper.Map(request, category);
            var after = Snapshot(category);
            if (before == after)
                await AuditAsync("CATEGORY_UPDATED", category, actor.AccountId, null, null, AuditOutcome.NoChange, token);
            else
            {
                unitOfWork.DeviceCategories.Update(category);
                await AuditAsync("CATEGORY_UPDATED", category, actor.AccountId, before, after, AuditOutcome.Success, token);
            }
            return true;
        }, ct);

    public async Task SetLockedAsync(string displayCode, bool locked, CancellationToken ct)
        => await WriteAsync(displayCode, locked ? "CATEGORY_LOCKED" : "CATEGORY_UNLOCKED", async token =>
        {
            var actor = await RequireActorAsync(true, token);
            var category = await FindAsync(displayCode, token);
            var status = locked ? EntityStatus.Locked : EntityStatus.Active;
            var action = locked ? "CATEGORY_LOCKED" : "CATEGORY_UNLOCKED";
            if (category.Status == status)
                await AuditAsync(action, category, actor.AccountId, null, null, AuditOutcome.NoChange, token);
            else
            {
                var before = Snapshot(category);
                category.Status = status;
                unitOfWork.DeviceCategories.Update(category);
                await AuditAsync(action, category, actor.AccountId, before, Snapshot(category), AuditOutcome.Success, token);
            }
            return true;
        }, ct);

    public Task<CategoryLockImpactDto> GetLockImpactAsync(string displayCode, CancellationToken ct)
        => ReadAsync(async () =>
        {
            await RequireActorAsync(true, ct);
            var category = await FindAsync(displayCode, ct);
            return new CategoryLockImpactDto { DisplayCode = category.DisplayCode, Status = category.Status };
        });

    public async Task DenyDeleteAsync(string displayCode, CancellationToken ct)
        => await WriteAsync<bool>(displayCode, "DELETE_DENIED", async token =>
        {
            await RequireActorAsync(true, token);
            await FindAsync(displayCode, token);
            throw Error(FailureKind.Forbidden, "CATEGORY_DELETE_FORBIDDEN", "Categories cannot be deleted. Use lock instead.");
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

    public Task<PagedResult<CategoryHistoryListItemDto>> ListHistoryAsync(CategoryHistoryQuery query, CancellationToken ct, string? scope = null)
        => ReadAsync(async () =>
        {
            await RequireActorAsync(true, ct);
            Validate(query);
            var categoryId = scope is null ? (Guid?)null : (await FindAsync(scope, ct)).Id;
            return await unitOfWork.CategoryHistories.ListAsync(query, ct, categoryId);
        });

    public Task<CategoryHistoryDetailDto> GetHistoryAsync(Guid historyId, CancellationToken ct, string? scope = null)
        => ReadAsync(async () =>
        {
            var actor = await RequireActorAsync(true, ct);
            var categoryId = scope is null ? (Guid?)null : (await FindAsync(scope, ct)).Id;
            var item = await unitOfWork.CategoryHistories.GetDetailAsync(historyId, ct, categoryId)
                ?? throw Error(FailureKind.NotFound, "CATEGORY_HISTORY_NOT_FOUND", "Category history was not found.");
            var detail = mapper.Map<CategoryHistoryDetailDto>(item);
            detail.RequestId = actor.RoleCode == RoleCodes.EquipmentManager ? item.RequestId : null;
            // Do not echo arbitrary values from imported or older audit records.
            detail.ReasonCode = item.ReasonCode is "VALIDATION_FAILED" or "AUTHENTICATION_REQUIRED" or "CATEGORY_ACCESS_FORBIDDEN"
                or "CATEGORY_DELETE_FORBIDDEN" or "CATEGORY_NAME_CONFLICT" or "CATEGORY_CODE_CONFLICT" or "CATEGORY_NOT_FOUND"
                or "CATEGORY_LOCKED" or "CATEGORY_VERSION_CONFLICT" or "CATEGORY_UNAVAILABLE" ? item.ReasonCode : null;
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
            throw Error(FailureKind.Forbidden, "CATEGORY_ACCESS_FORBIDDEN", "Category access is not permitted.");
        return new(id, sessionId, role.Code);
    }

    private async Task<DeviceCategory> FindAsync(string displayCode, CancellationToken ct)
        => await unitOfWork.DeviceCategories.GetByDisplayCodeAsync(NormalizeCode(displayCode), ct)
            ?? throw Error(FailureKind.NotFound, "CATEGORY_NOT_FOUND", "Category was not found.");

    private async Task RequireUniqueNameAsync(string name, Guid? id, CancellationToken ct)
    {
        if (await unitOfWork.DeviceCategories.NameExistsAsync(name, id, ct))
            throw Error(FailureKind.Conflict, "CATEGORY_NAME_CONFLICT", "A category with this name already exists.");
    }

    private async Task<string> GenerateCodeAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 32; attempt++)
        {
            var code = "DM" + Guid.NewGuid().ToString("N").ToUpperInvariant();
            if (!await unitOfWork.DeviceCategories.DisplayCodeExistsAsync(code, null, ct)) return code;
        }
        throw Unavailable();
    }

    private Task AuditAsync(string action, DeviceCategory category, Guid actor, string? before, string? after, AuditOutcome outcome, CancellationToken ct)
    {
        var snapshot = Snapshot(category);
        return unitOfWork.CategoryHistories.AddAsync(new CategoryHistory
        {
            RequestId = requestContext.RequestId, ActorAccountId = actor, CategoryId = category.Id,
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
            await SaveFailureAsync(code, action, AuditOutcome.Failed, "CATEGORY_UNAVAILABLE", ct);
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
                DeviceCategory? category = null;
                if (code is not null)
                {
                    try { category = await unitOfWork.DeviceCategories.GetByDisplayCodeAsync(NormalizeCode(code), token); }
                    catch (UseCaseException) { /* Invalid route values are never copied into snapshots. */ }
                }
                await unitOfWork.CategoryHistories.AddAsync(new CategoryHistory
                {
                    RequestId = requestContext.RequestId, ActorAccountId = actor, CategoryId = category?.Id,
                    ActionCode = action, Outcome = outcome, ReasonCode = reason, OccurredAt = clock.GetUtcNow(),
                    BeforeData = category is null ? null : Snapshot(category)
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
        "Category persistence failed. RequestId: {RequestId}, ErrorType: {ErrorType}", requestContext.RequestId, exception.GetType().Name);

    private static string NormalizeCode(string value)
    {
        var code = BusinessCode.Normalize(Uri.UnescapeDataString(value));
        if (code is null || code.Length > 64 || !Regex.IsMatch(code, BusinessCode.Pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            throw Error(FailureKind.Validation, "VALIDATION_FAILED", "Category display code is invalid.");
        return code;
    }

    private static string Snapshot(DeviceCategory category) => JsonSerializer.Serialize(new
    {
        display_code = category.DisplayCode, name = category.Name, description = category.Description,
        notes = category.Notes, status = category.Status.ToString()
    });

    private static string Version(DeviceCategory category)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Snapshot(category))));

    private static IReadOnlyDictionary<string, JsonElement>? SafeSnapshot(string? json)
    {
        if (json is null) return null;
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
        var result = new Dictionary<string, JsonElement>();
        foreach (var field in SnapshotFields)
            if (document.RootElement.TryGetProperty(field, out var value)
                && (value.ValueKind == JsonValueKind.Null || value.ValueKind == JsonValueKind.String
                    && value.GetString()!.Length <= (field is "description" or "notes" ? 4000 : field == "name" ? 200 : 64)))
                result[field] = value.Clone();
        return result;
    }

    private static string[] Changes(IReadOnlyDictionary<string, JsonElement> before, IReadOnlyDictionary<string, JsonElement> after)
        => SnapshotFields.Where(field => before.GetValueOrDefault(field).ToString() != after.GetValueOrDefault(field).ToString()).ToArray();

    private static bool IsEquipmentRole(string role) => role is RoleCodes.EquipmentManager or RoleCodes.EquipmentStaff;
    private static void Validate(object value)
    {
        if (!Validator.TryValidateObject(value, new ValidationContext(value), [], true))
            throw Error(FailureKind.Validation, "VALIDATION_FAILED", "Category request is invalid.");
    }
    private static UseCaseException AuthenticationRequired() => Error(FailureKind.Unauthorized, "AUTHENTICATION_REQUIRED", "A valid session is required.");
    private static UseCaseException Unavailable() => Error(FailureKind.Unavailable, "CATEGORY_UNAVAILABLE", "Categories are temporarily unavailable.");
    private static UseCaseException Error(FailureKind kind, string code, string message) => new(kind, code, message);
}
