using CSVC.Core.Accounts;
using CSVC.Core.Common;
using CSVC.Core.DeviceTransfers;
using CSVC.Core.Settings;
using CSVC.UseCases.Abstractions;
using CSVC.UseCases.Abstractions.Persistence;
using CSVC.UseCases.Abstractions.Security;
using CSVC.UseCases.Exceptions;

namespace CSVC.UseCases.Settings;

public sealed class EquipmentSettingsService(IRepository<EquipmentSettings> settings,
    IRepository<EquipmentSettingsHistory> history, IUnitOfWork uow, IInventoryTransaction transaction,
    ICurrentAccount current, IRequestContext requestContext, TimeProvider clock) : IEquipmentSettingsService
{
    public async Task<EquipmentSettingsDto> GetAsync(CancellationToken ct)
    {
        await RequireActorAsync(true, ct);
        var config = await settings.FirstOrDefaultAsync(x => x.Id == 1, ct);
        var department = config?.EquipmentDepartmentId is { } id ? await uow.Departments.GetByIdAsync(id, ct) : null;
        var mismatches = new List<EquipmentAccountMismatchDto>();
        if (department is not null)
        {
            var roles = (await uow.Roles.GetAllAsync(ct)).Where(x => x.Code is RoleCodes.EquipmentManager or RoleCodes.EquipmentStaff).ToDictionary(x => x.Id);
            var roleIds = roles.Keys.ToArray();
            var accounts = await uow.Accounts.FindAsync(x => roleIds.Contains(x.RoleId) && !x.IsLock, ct);
            var profileIds = accounts.Select(x => x.UserProfileId).ToArray();
            var profiles = (await uow.UserProfiles.FindAsync(x => profileIds.Contains(x.Id) && x.DepartmentId != department.Id, ct)).ToDictionary(x => x.Id);
            var departmentIds = profiles.Values.Select(x => x.DepartmentId).Distinct().ToArray();
            var departments = (await uow.Departments.FindAsync(x => departmentIds.Contains(x.Id), ct)).ToDictionary(x => x.Id);
            foreach (var account in accounts.OrderBy(x => x.AccountCode))
                if (profiles.TryGetValue(account.UserProfileId, out var profile))
                    mismatches.Add(new(account.Id, account.AccountCode, profile.FullName, roles[account.RoleId].Code,
                        departments.GetValueOrDefault(profile.DepartmentId)?.Name ?? ""));
        }
        return new(department?.Status == EntityStatus.Active, department?.Code, department?.Name, config?.Version ?? 0, config?.UpdatedAt, mismatches);
    }

    public async Task UpdateAsync(UpdateEquipmentDepartmentRequest request, CancellationToken ct)
        => await transaction.ExecuteAsync(async token =>
        {
            var actor = await RequireActorAsync(true, token);
            if (string.IsNullOrWhiteSpace(request.DepartmentCode) || request.DepartmentCode.Length > 32 || request.Version < 0)
                throw new UseCaseException(FailureKind.Validation, "VALIDATION_FAILED", "Select a department and provide the current version.");
            var code = request.DepartmentCode.Trim().ToLowerInvariant();
            var department = await uow.Departments.FirstOrDefaultAsync(x => x.Code.ToLower() == code, token);
            if (department is null || department.Status != EntityStatus.Active)
                throw new UseCaseException(FailureKind.Conflict, "INVALID_DEPARTMENT", "Select an active department.");
            var config = await settings.FirstOrDefaultTrackedAsync(x => x.Id == 1, token);
            if ((config?.Version ?? 0) != request.Version)
                throw new UseCaseException(FailureKind.Conflict, "VERSION_CONFLICT", "Settings changed. Reload before saving.");
            if (config?.EquipmentDepartmentId == department.Id) return true;
            if (config?.EquipmentDepartmentId is not null && await uow.DeviceTransfers.ExistsAsync(x =>
                x.Status == DeviceTransferStatus.PendingConfirmation || x.Status == DeviceTransferStatus.PendingApproval || x.Status == DeviceTransferStatus.Approved, token))
                throw new UseCaseException(FailureKind.Conflict, "EQUIPMENT_DEPARTMENT_HAS_OPEN_TRANSFERS", "Finish or cancel open transfers before changing the equipment department.");
            var previous = config?.EquipmentDepartmentId;
            if (config is null) { config = new(); await settings.AddAsync(config, token); }
            config.EquipmentDepartmentId = department.Id;
            config.Version++;
            config.UpdatedByAccountId = actor.AccountId;
            config.UpdatedAt = clock.GetUtcNow();
            await history.AddAsync(new EquipmentSettingsHistory { RequestId = requestContext.RequestId, ActorAccountId = actor.AccountId,
                PreviousDepartmentId = previous, DepartmentId = department.Id, OccurredAt = clock.GetUtcNow() }, token);
            return true;
        }, ct);

    public async Task<EquipmentWorkflowContextDto> ContextAsync(CancellationToken ct)
    {
        var actor = await RequireActorAsync(false, ct);
        var config = await settings.FirstOrDefaultAsync(x => x.Id == 1, ct);
        var department = config?.EquipmentDepartmentId is { } id ? await uow.Departments.GetByIdAsync(id, ct) : null;
        var actorDepartment = await uow.Departments.GetByIdAsync(actor.DepartmentId, ct);
        return new(department?.Status == EntityStatus.Active, department?.Code,
            actor.RoleCode == RoleCodes.DepartmentStaff || department?.Id == actor.DepartmentId,
            actor.DepartmentId, actorDepartment?.Code ?? "");
    }

    private async Task<(Guid AccountId, string RoleCode, Guid DepartmentId)> RequireActorAsync(bool manager, CancellationToken ct)
    {
        if (current.AccountId is not { } id || current.SessionId is not { } sessionId)
            throw new UseCaseException(FailureKind.Unauthorized, "AUTHENTICATION_REQUIRED", "A valid session is required.");
        var account = await uow.Accounts.FirstOrDefaultAsync(x => x.Id == id && !x.IsLock, ct);
        if (account is null || !await uow.AuthSessions.ExistsAsync(x => x.Id == sessionId && x.AccountId == id && x.IsActive && x.ExpiresAt > clock.GetUtcNow(), ct))
            throw new UseCaseException(FailureKind.Unauthorized, "AUTHENTICATION_REQUIRED", "A valid session is required.");
        var role = await uow.Roles.GetByIdAsync(account.RoleId, ct);
        if (role?.Code is not (RoleCodes.EquipmentManager or RoleCodes.EquipmentStaff or RoleCodes.DepartmentStaff) || manager && role.Code != RoleCodes.EquipmentManager)
            throw new UseCaseException(FailureKind.Forbidden, "SETTINGS_ACTION_FORBIDDEN", "Only equipment managers may manage Settings.");
        var profile = await uow.UserProfiles.GetByIdAsync(account.UserProfileId, ct)
            ?? throw new UseCaseException(FailureKind.Unauthorized, "AUTHENTICATION_REQUIRED", "An account profile is required.");
        return (id, role.Code, profile.DepartmentId);
    }
}
