using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using AutoMapper;
using CSVC.Core.Accounts;
using CSVC.Core.Areas;
using CSVC.Core.Common;
using CSVC.UseCases.Abstractions;
using CSVC.UseCases.Abstractions.Persistence;
using CSVC.UseCases.Abstractions.Security;
using CSVC.UseCases.Areas.DTOs;
using CSVC.UseCases.Common;
using CSVC.UseCases.Exceptions;
using Microsoft.Extensions.Logging;

namespace CSVC.UseCases.Areas;

public sealed class AreaService(
    IUnitOfWork unitOfWork, IAreaTransaction transaction, ICurrentAccount current,
    IRequestContext requestContext, IMapper mapper, TimeProvider clock, ILogger<AreaService> logger) : IAreaService
{
    public async Task<PagedResult<AreaListItemDto>> ListAsync(AreaListQuery query, CancellationToken cancellationToken)
    {
        await RequireActorAsync(false, cancellationToken);
        Validate(query);
        // Load roots initially; a search locates matching nodes at any depth, including their paths.
        return await unitOfWork.Areas.ListAsync(query, null, string.IsNullOrWhiteSpace(query.Search), cancellationToken);
    }

    public async Task<PagedResult<AreaListItemDto>> GetChildrenAsync(Guid id, AreaListQuery query, CancellationToken cancellationToken)
    {
        await RequireActorAsync(false, cancellationToken);
        Validate(query);
        await RequireAreaAsync(id, cancellationToken);
        return await unitOfWork.Areas.ListAsync(query, id, true, cancellationToken);
    }

    public async Task<AreaDetailDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(false, cancellationToken);
        var result = await RequireAreaAsync(id, cancellationToken);
        result.Children = await unitOfWork.Areas.ListAsync(new(), id, true, cancellationToken);
        if (actor.RoleCode == RoleCodes.EquipmentManager)
            result.Management = await unitOfWork.Areas.GetManagementAsync(id, cancellationToken);
        return result;
    }

    public Task<AreaDetailDto> CreateAsync(CreateAreaRequest request, CancellationToken cancellationToken)
        => WriteAsync(null, async ct =>
        {
            var actor = await RequireActorAsync(true, ct);
            Validate(request);
            await RequireParentAsync(request.ParentId, null, ct);
            await RequireUniqueNameAsync(request.Name, request.ParentId, null, ct);
            var area = mapper.Map<Area>(request);
            area.Code = "KV" + Guid.NewGuid().ToString("N")[..30];
            area.ParentId = request.ParentId;
            area.Status = EntityStatus.Active;
            await unitOfWork.Areas.AddAsync(area, ct);
            // Flush within the explicit transaction to read the resulting recursive path.
            // A later audit failure still rolls back this insert.
            await unitOfWork.CommitAsync(ct);
            var result = await RequireAreaAsync(area.Id, ct);
            await AuditAsync("AREA_CREATED", area.Id, actor.AccountId, ["name", "typeLabel", "description", "parentId", "status"],
                null, Snapshot(result, 0), ct);
            result.Management = new() { CreatedBy = actor.AccountId, CreatedAt = clock.GetUtcNow() };
            return result;
        }, cancellationToken);

    public async Task UpdateAsync(Guid id, UpdateAreaRequest request, CancellationToken cancellationToken)
        => await WriteAsync(id, async ct =>
        {
            var actor = await RequireActorAsync(true, ct);
            Validate(request);
            var before = await RequireAreaAsync(id, ct);
            if (before.Status == EntityStatus.Locked)
                throw Error(FailureKind.Conflict, "AREA_LOCKED", "Unlock this area before editing or moving it.");
            var parentId = request.HasParentChange() ? request.ParentId : before.ParentId;
            await RequireParentAsync(parentId, id, ct);
            await RequireUniqueNameAsync(request.Name, parentId, id, ct);
            var entity = await unitOfWork.Areas.GetByIdAsync(id, ct) ?? throw Missing();
            mapper.Map(request, entity);
            entity.ParentId = parentId;
            var fields = ChangedFields(before, entity);
            if (fields.Count == 0) return true;
            var descendants = await unitOfWork.Areas.CountDescendantsAsync(id, ct);
            unitOfWork.Areas.Update(entity);
            await unitOfWork.CommitAsync(ct);
            var after = await RequireAreaAsync(id, ct);
            await AuditAsync(parentId != before.ParentId ? "AREA_REPARENTED" : "AREA_UPDATED", id, actor.AccountId,
                fields, Snapshot(before, descendants), Snapshot(after, descendants), ct);
            return true;
        }, cancellationToken);

    public async Task SetLockedAsync(Guid id, bool locked, CancellationToken cancellationToken)
        => await WriteAsync(id, async ct =>
        {
            var actor = await RequireActorAsync(true, ct);
            var before = await RequireAreaAsync(id, ct);
            var status = locked ? EntityStatus.Locked : EntityStatus.Active;
            if (before.Status == status) return true;
            var descendants = await unitOfWork.Areas.CountDescendantsAsync(id, ct);
            var entity = await unitOfWork.Areas.GetByIdAsync(id, ct) ?? throw Missing();
            entity.Status = status;
            unitOfWork.Areas.Update(entity);
            await unitOfWork.CommitAsync(ct);
            var after = await RequireAreaAsync(id, ct);
            await AuditAsync(locked ? "AREA_LOCKED" : "AREA_UNLOCKED", id, actor.AccountId, ["status"],
                Snapshot(before, descendants), Snapshot(after, descendants), ct);
            return true;
        }, cancellationToken);

    public async Task<AreaLockImpactDto> GetLockImpactAsync(Guid id, CancellationToken cancellationToken)
    {
        await RequireActorAsync(true, cancellationToken);
        var area = await RequireAreaAsync(id, cancellationToken);
        return new()
        {
            AreaId = id, Status = area.Status, EffectiveStatus = area.EffectiveStatus, ChildCount = area.ChildCount,
            DescendantCount = await unitOfWork.Areas.CountDescendantsAsync(id, cancellationToken)
        };
    }

    public async Task DenyDeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await SaveFailureAsync(id, "DELETE_DENIED", AuditOutcome.Denied, "AREA_DELETE_FORBIDDEN", cancellationToken);
        throw Error(FailureKind.Forbidden, "AREA_DELETE_FORBIDDEN", "Areas cannot be deleted. Use lock instead.");
    }

    public Task RecordInvalidRequestAsync(Guid? id, CancellationToken cancellationToken)
        => SaveFailureAsync(id, "FAILED", AuditOutcome.Failed, "VALIDATION_FAILED", cancellationToken);

    private async Task RequireParentAsync(Guid? parentId, Guid? sourceId, CancellationToken ct)
    {
        if (parentId is null) return;
        if (parentId == Guid.Empty)
            throw Error(FailureKind.Validation, "INVALID_AREA_PARENT", "Select an existing area or null for a root.");
        if (parentId == sourceId) throw Error(FailureKind.Conflict, "AREA_CYCLE", "An area cannot be its own parent.");
        var parent = await unitOfWork.Areas.GetDetailAsync(parentId.Value, ct)
            ?? throw Error(FailureKind.NotFound, "AREA_PARENT_NOT_FOUND", "The selected parent area was not found.");
        if (sourceId is { } id && parent.PathIds.Contains(id))
            throw Error(FailureKind.Conflict, "AREA_CYCLE", "An area cannot be moved below its descendant.");
        if (parent.EffectiveStatus != EntityStatus.Active)
            throw Error(FailureKind.Conflict, "AREA_PARENT_UNAVAILABLE", "The selected parent or one of its ancestors is locked.");
    }

    private async Task RequireUniqueNameAsync(string name, Guid? parentId, Guid? exceptId, CancellationToken ct)
    {
        if (await unitOfWork.Areas.NameExistsAsync(name, parentId, exceptId, ct))
            throw Error(FailureKind.Conflict, "AREA_NAME_CONFLICT", "An area with this name already exists under the selected parent.");
    }

    private async Task<AreaDetailDto> RequireAreaAsync(Guid id, CancellationToken ct)
        => await unitOfWork.Areas.GetDetailAsync(id, ct) ?? throw Missing();

    private async Task<SessionIdentity> RequireActorAsync(bool managerOnly, CancellationToken ct)
    {
        if (current.AccountId is not { } id || current.SessionId is not { } sessionId)
            throw Error(FailureKind.Unauthorized, "AUTHENTICATION_REQUIRED", "A valid session is required.");
        var account = await unitOfWork.Accounts.FirstOrDefaultAsync(a => a.Id == id && !a.IsLock, ct);
        var session = await unitOfWork.AuthSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.AccountId == id
            && s.IsActive && s.ExpiresAt > clock.GetUtcNow(), ct);
        if (account is null || session is null)
            throw Error(FailureKind.Unauthorized, "AUTHENTICATION_REQUIRED", "A valid session is required.");
        var role = await unitOfWork.Roles.GetByIdAsync(account.RoleId, ct);
        if (role is null || role.Code is not (RoleCodes.EquipmentManager or RoleCodes.EquipmentStaff or RoleCodes.DepartmentStaff)
            || managerOnly && role.Code != RoleCodes.EquipmentManager)
            throw Error(FailureKind.Forbidden, "AREA_ACCESS_FORBIDDEN", "Area access is not permitted.");
        return new(id, sessionId, role.Code);
    }

    private Task AuditAsync(string action, Guid id, Guid actor, IReadOnlyList<string> fields, string? before, string after, CancellationToken ct)
        => unitOfWork.AreaHistories.AddAsync(new AreaHistory
        {
            RequestId = requestContext.RequestId, ActorAccountId = actor, AreaId = id,
            ActionCode = action, Outcome = AuditOutcome.Success, OccurredAt = clock.GetUtcNow(),
            ChangedFields = JsonSerializer.Serialize(fields), BeforeData = before, AfterData = after
        }, ct);

    private async Task<T> WriteAsync<T>(Guid? id, Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        try { return await transaction.ExecuteAsync(action, ct); }
        catch (UseCaseException failure)
        {
            var denied = failure.Kind is FailureKind.Forbidden or FailureKind.Unauthorized;
            await SaveFailureAsync(id, denied ? "DENIED" : "FAILED", denied ? AuditOutcome.Denied : AuditOutcome.Failed, failure.Code, ct);
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError("Area persistence failed. RequestId: {RequestId}, ErrorType: {ErrorType}", requestContext.RequestId, exception.GetType().Name);
            await SaveFailureAsync(id, "FAILED", AuditOutcome.Failed, "AREA_UNAVAILABLE", ct);
            throw Error(FailureKind.Unavailable, "AREA_UNAVAILABLE", "Area changes are temporarily unavailable.");
        }
    }

    private async Task SaveFailureAsync(Guid? id, string action, AuditOutcome outcome, string reason, CancellationToken ct)
    {
        try
        {
            await transaction.ExecuteAsync(async token =>
            {
                var actor = current.AccountId is { } a && await unitOfWork.Accounts.ExistsAsync(x => x.Id == a, token) ? (Guid?)a : null;
                var target = id is { } area && await unitOfWork.Areas.ExistsAsync(x => x.Id == area, token) ? (Guid?)area : null;
                await unitOfWork.AreaHistories.AddAsync(new AreaHistory
                {
                    RequestId = requestContext.RequestId, ActorAccountId = actor, AreaId = target,
                    ActionCode = action, Outcome = outcome, ReasonCode = reason, OccurredAt = clock.GetUtcNow()
                }, token);
                return true;
            }, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError("Area audit failed. RequestId: {RequestId}, ErrorType: {ErrorType}", requestContext.RequestId, exception.GetType().Name);
            throw Error(FailureKind.Unavailable, "AREA_UNAVAILABLE", "Area changes are temporarily unavailable.");
        }
    }

    private static List<string> ChangedFields(AreaDetailDto before, Area after)
    {
        List<string> fields = [];
        if (before.Name != after.Name) fields.Add("name");
        if (before.TypeLabel != after.TypeLabel) fields.Add("typeLabel");
        if (before.Description != after.Description) fields.Add("description");
        if (before.ParentId != after.ParentId) fields.Add("parentId");
        return fields;
    }

    private static string Snapshot(AreaDetailDto a, int descendants) => JsonSerializer.Serialize(new
    {
        id = a.Id, code = a.Code, parentId = a.ParentId, name = a.Name, typeLabel = a.TypeLabel, description = a.Description,
        status = a.Status.ToString(), effectiveStatus = a.EffectiveStatus.ToString(),
        pathIds = a.PathIds, pathNames = a.PathNames, affectedDescendantCount = descendants
    });

    private static void Validate(object value)
    {
        if (!Validator.TryValidateObject(value, new ValidationContext(value), [], true))
            throw Error(FailureKind.Validation, "VALIDATION_FAILED", "Area request is invalid.");
    }
    private static UseCaseException Missing() => Error(FailureKind.NotFound, "AREA_NOT_FOUND", "Area was not found.");
    private static UseCaseException Error(FailureKind kind, string code, string message) => new(kind, code, message);
}
