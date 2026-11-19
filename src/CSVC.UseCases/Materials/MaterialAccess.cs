using CSVC.Core.Accounts;
using CSVC.UseCases.Abstractions.Persistence;
using CSVC.UseCases.Abstractions.Security;
using CSVC.UseCases.Exceptions;
using CSVC.UseCases.Settings;

namespace CSVC.UseCases.Materials;

public sealed record MaterialActor(Guid Id, string AccountCode, string FullName, string Role, Guid DepartmentId)
{
    public bool Manager => Role == RoleCodes.EquipmentManager;
    public bool Staff => Role == RoleCodes.EquipmentStaff;
    public bool Department => Role == RoleCodes.DepartmentStaff;
}
public sealed class MaterialAccess(IUnitOfWork uow, ICurrentAccount current, IEquipmentDepartmentPolicy equipment, TimeProvider clock)
{
    public async Task<MaterialActor> RequireAsync(CancellationToken ct, bool manager = false, bool allowDepartment = false)
    {
        if (current.AccountId is not { } id || current.SessionId is not { } session) throw Unauthorized();
        var account = await uow.Accounts.FirstOrDefaultAsync(a => a.Id == id && !a.IsLock, ct) ?? throw Unauthorized();
        if (!await uow.AuthSessions.ExistsAsync(s => s.Id == session && s.AccountId == id && s.IsActive && s.ExpiresAt > clock.GetUtcNow(), ct)) throw Unauthorized();
        var role = await uow.Roles.GetByIdAsync(account.RoleId, ct);
        if (role?.Code is not (RoleCodes.EquipmentManager or RoleCodes.EquipmentStaff) && !(allowDepartment && role?.Code == RoleCodes.DepartmentStaff)) throw MaterialErrors.Forbidden();
        if (manager && role!.Code != RoleCodes.EquipmentManager) throw MaterialErrors.Forbidden();
        var profile = await uow.UserProfiles.GetByIdAsync(account.UserProfileId, ct) ?? throw Unauthorized();
        if (role!.Code != RoleCodes.DepartmentStaff) await equipment.RequireMemberAsync(role.Code, profile.DepartmentId, ct);
        return new(id, account.AccountCode, profile.FullName, role.Code, profile.DepartmentId);
    }
    private static UseCaseException Unauthorized() => new(FailureKind.Unauthorized, "AUTHENTICATION_REQUIRED", "Sign in with an active session.");
}
public static class MaterialErrors
{
    public static UseCaseException Invalid(string message) => new(FailureKind.Validation, "MATERIAL_INVALID", message);
    public static UseCaseException Missing(string message = "The material record does not exist.") => new(FailureKind.NotFound, "MATERIAL_NOT_FOUND", message);
    public static UseCaseException Conflict(string code, string message) => new(FailureKind.Conflict, code, message);
    public static UseCaseException Forbidden(string message = "This material operation is not permitted for this account.") => new(FailureKind.Forbidden, "MATERIAL_ACTION_FORBIDDEN", message);
}
