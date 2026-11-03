using CSVC.Core.Accounts;
using CSVC.Core.Common;
using CSVC.Core.Settings;
using CSVC.UseCases.Abstractions.Persistence;
using CSVC.UseCases.Exceptions;

namespace CSVC.UseCases.Settings;

public sealed class EquipmentDepartmentPolicy(IRepository<EquipmentSettings> settings, IUnitOfWork uow) : IEquipmentDepartmentPolicy
{
    public async Task<Guid> RequireConfiguredAsync(CancellationToken ct)
    {
        var config = await settings.FirstOrDefaultAsync(x => x.Id == 1, ct);
        if (config?.EquipmentDepartmentId is not { } id)
            throw new UseCaseException(FailureKind.Conflict, "EQUIPMENT_DEPARTMENT_NOT_CONFIGURED", "Select the equipment department in Settings first.");
        if (!await uow.Departments.ExistsAsync(x => x.Id == id && x.Status == EntityStatus.Active, ct))
            throw new UseCaseException(FailureKind.Conflict, "EQUIPMENT_DEPARTMENT_UNAVAILABLE", "The configured equipment department is unavailable.");
        return id;
    }

    public async Task RequireMemberAsync(string roleCode, Guid departmentId, CancellationToken ct)
    {
        var configuredId = await RequireConfiguredAsync(ct);
        if ((roleCode is RoleCodes.EquipmentManager or RoleCodes.EquipmentStaff) && departmentId != configuredId)
            throw new UseCaseException(FailureKind.Forbidden, "EQUIPMENT_ACCOUNT_DEPARTMENT_MISMATCH", "Update the equipment account's department to match Settings.");
    }

    public async Task ValidateAssignmentAsync(string roleCode, Guid departmentId, CancellationToken ct)
    {
        if (roleCode is not (RoleCodes.EquipmentManager or RoleCodes.EquipmentStaff)) return;
        var config = await settings.FirstOrDefaultAsync(x => x.Id == 1, ct);
        // Initial account/department setup must remain possible before a department is selected.
        if (config?.EquipmentDepartmentId is not null)
            await RequireMemberAsync(roleCode, departmentId, ct);
    }
}
