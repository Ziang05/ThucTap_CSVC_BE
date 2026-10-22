using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using AutoMapper;
using CSVC.Core.Accounts;
using CSVC.Core.Common;
using CSVC.Core.Departments;
using CSVC.UseCases.Abstractions;
using CSVC.UseCases.Abstractions.Persistence;
using CSVC.UseCases.Abstractions.Security;
using CSVC.UseCases.Common;
using CSVC.UseCases.Departments.DTOs;
using CSVC.UseCases.Exceptions;
using Microsoft.Extensions.Logging;

namespace CSVC.UseCases.Departments;

public sealed class DepartmentService(
    IUnitOfWork unitOfWork, IDepartmentTransaction transaction, ICurrentAccount current,
    IRequestContext requestContext, IMapper mapper, TimeProvider clock, ILogger<DepartmentService> logger,
    IBusinessCodeGenerator codes) : IDepartmentService
{
    public async Task<PagedResult<DepartmentListItemDto>> ListAsync(DepartmentListQuery query, CancellationToken cancellationToken)
    {
        await RequireActorAsync(false, cancellationToken);
        Validate(query);
        return await unitOfWork.Departments.ListAsync(query, cancellationToken);
    }

    public async Task<DepartmentDetailDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(false, cancellationToken);
        var result = await unitOfWork.Departments.GetDetailAsync(id, cancellationToken) ?? throw Missing();
        if (actor.RoleCode == RoleCodes.EquipmentManager)
            result.Management = await unitOfWork.Departments.GetManagementAsync(id, cancellationToken);
        return result;
    }

    public Task<DepartmentDetailDto> CreateAsync(CreateDepartmentRequest request, CancellationToken cancellationToken)
        => WriteAsync(null, async ct =>
        {
            var actor = await RequireActorAsync(true, ct);
            Validate(request);
            await RequireUniqueNameAsync(request.Name, null, ct);
            var department = mapper.Map<Department>(request);
            department.Code = request.Code ?? await codes.NextDepartmentCodeAsync(ct);
            await RequireUniqueCodeAsync(department.Code, null, ct);
            department.Status = EntityStatus.Active;
            await unitOfWork.Departments.AddAsync(department, ct);
            await AuditAsync("DEPARTMENT_CREATED", department.Id, actor.AccountId, null, Snapshot(department), ct);
            var detail = mapper.Map<DepartmentDetailDto>(department);
            detail.Management = new() { CreatedBy = actor.AccountId, CreatedAt = clock.GetUtcNow(), AccountCount = 0 };
            return detail;
        }, cancellationToken);

    public async Task UpdateAsync(Guid id, UpdateDepartmentRequest request, CancellationToken cancellationToken)
        => await WriteAsync(id, async ct =>
        {
            var actor = await RequireActorAsync(true, ct);
            Validate(request);
            var department = await unitOfWork.Departments.GetByIdAsync(id, ct) ?? throw Missing();
            if (department.Status != EntityStatus.Active)
                throw Error(FailureKind.Conflict, "DEPARTMENT_LOCKED", "Unlock this department before editing it.");
            await RequireUniqueNameAsync(request.Name, id, ct);
            if (request.Code is not null) await RequireUniqueCodeAsync(request.Code, id, ct);
            var before = Snapshot(department);
            mapper.Map(request, department);
            if (request.Code is not null) department.Code = request.Code;
            var after = Snapshot(department);
            if (before == after) return true;
            unitOfWork.Departments.Update(department);
            await AuditAsync("DEPARTMENT_UPDATED", id, actor.AccountId, before, after, ct);
            return true;
        }, cancellationToken);

    public async Task SetLockedAsync(Guid id, bool locked, CancellationToken cancellationToken)
        => await WriteAsync(id, async ct =>
        {
            var actor = await RequireActorAsync(true, ct);
            var department = await unitOfWork.Departments.GetByIdAsync(id, ct) ?? throw Missing();
            var status = locked ? EntityStatus.Locked : EntityStatus.Active;
            if (department.Status == status) return true;
            var before = Snapshot(department);
            department.Status = status;
            unitOfWork.Departments.Update(department);
            await AuditAsync(locked ? "DEPARTMENT_LOCKED" : "DEPARTMENT_UNLOCKED", id, actor.AccountId,
                before, Snapshot(department), ct);
            return true;
        }, cancellationToken);

    public async Task<DepartmentLockImpactDto> GetLockImpactAsync(Guid id, CancellationToken cancellationToken)
    {
        await RequireActorAsync(true, cancellationToken);
        var department = await unitOfWork.Departments.GetDetailAsync(id, cancellationToken) ?? throw Missing();
        var management = await unitOfWork.Departments.GetManagementAsync(id, cancellationToken);
        return new() { DepartmentId = id, Status = department.Status, AccountCount = management.AccountCount };
    }

    public async Task<PagedResult<DepartmentHistoryDto>> GetHistoryAsync(Guid id, DepartmentHistoryQuery query, CancellationToken cancellationToken)
    {
        await RequireActorAsync(true, cancellationToken);
        Validate(query);
        if (!await unitOfWork.Departments.ExistsAsync(d => d.Id == id, cancellationToken)) throw Missing();
        return await unitOfWork.DepartmentHistories.ListAsync(id, query, cancellationToken);
    }

    public async Task DenyDeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await SaveFailureAsync(id, "DELETE_DENIED", AuditOutcome.Denied, "DEPARTMENT_DELETE_FORBIDDEN", cancellationToken);
        throw Error(FailureKind.Forbidden, "DEPARTMENT_DELETE_FORBIDDEN", "Departments cannot be deleted. Use lock instead.");
    }

    public Task RecordInvalidRequestAsync(Guid? id, CancellationToken cancellationToken)
        => SaveFailureAsync(id, "FAILED", AuditOutcome.Failed, "VALIDATION_FAILED", cancellationToken);

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
            throw Error(FailureKind.Forbidden, "DEPARTMENT_ACCESS_FORBIDDEN", "Department access is not permitted.");
        return new(id, sessionId, role.Code);
    }

    private async Task RequireUniqueNameAsync(string name, Guid? id, CancellationToken ct)
    {
        if (await unitOfWork.Departments.NameExistsAsync(name, id, ct))
            throw Error(FailureKind.Conflict, "DEPARTMENT_NAME_CONFLICT", "A department with this name already exists.");
    }

    private async Task RequireUniqueCodeAsync(string code, Guid? id, CancellationToken ct)
    {
        if (await unitOfWork.Departments.CodeExistsAsync(code, id, ct))
            throw Error(FailureKind.Conflict, "DEPARTMENT_CODE_CONFLICT", "A department with this code already exists.");
    }

    private Task AuditAsync(string action, Guid id, Guid actor, string? before, string after, CancellationToken ct)
        => unitOfWork.DepartmentHistories.AddAsync(new DepartmentHistory
        {
            RequestId = requestContext.RequestId, ActorAccountId = actor, DepartmentId = id,
            ActionCode = action, Outcome = AuditOutcome.Success, OccurredAt = clock.GetUtcNow(),
            BeforeData = before, AfterData = after
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
            logger.LogError("Department persistence failed. RequestId: {RequestId}, ErrorType: {ErrorType}",
                requestContext.RequestId, exception.GetType().Name);
            await SaveFailureAsync(id, "FAILED", AuditOutcome.Failed, "DEPARTMENT_UNAVAILABLE", ct);
            throw Error(FailureKind.Unavailable, "DEPARTMENT_UNAVAILABLE", "Department changes are temporarily unavailable.");
        }
    }

    private async Task SaveFailureAsync(Guid? id, string action, AuditOutcome outcome, string reason, CancellationToken ct)
    {
        try
        {
            await transaction.ExecuteAsync(async token =>
            {
                var actor = current.AccountId is { } a && await unitOfWork.Accounts.ExistsAsync(x => x.Id == a, token) ? (Guid?)a : null;
                var target = id is { } d && await unitOfWork.Departments.ExistsAsync(x => x.Id == d, token) ? (Guid?)d : null;
                await unitOfWork.DepartmentHistories.AddAsync(new DepartmentHistory
                {
                    RequestId = requestContext.RequestId, ActorAccountId = actor, DepartmentId = target,
                    ActionCode = action, Outcome = outcome, ReasonCode = reason, OccurredAt = clock.GetUtcNow()
                }, token);
                return true;
            }, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError("Department audit failed. RequestId: {RequestId}, ErrorType: {ErrorType}", requestContext.RequestId, exception.GetType().Name);
            throw Error(FailureKind.Unavailable, "DEPARTMENT_UNAVAILABLE", "Department changes are temporarily unavailable.");
        }
    }

    private static string Snapshot(Department d) => JsonSerializer.Serialize(new
        { id = d.Id, code = d.Code, name = d.Name, shortName = d.ShortName, description = d.Description, status = d.Status.ToString() });

    private static void Validate(object value)
    {
        if (!Validator.TryValidateObject(value, new ValidationContext(value), [], true))
            throw Error(FailureKind.Validation, "VALIDATION_FAILED", "Department request is invalid.");
    }

    private static UseCaseException Missing() => Error(FailureKind.NotFound, "DEPARTMENT_NOT_FOUND", "Department was not found.");
    private static UseCaseException Error(FailureKind kind, string code, string message) => new(kind, code, message);
}
