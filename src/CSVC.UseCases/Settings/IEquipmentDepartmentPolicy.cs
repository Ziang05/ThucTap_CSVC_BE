namespace CSVC.UseCases.Settings;

public interface IEquipmentDepartmentPolicy
{
    Task<Guid> RequireConfiguredAsync(CancellationToken ct);
    Task RequireMemberAsync(string roleCode, Guid departmentId, CancellationToken ct);
    Task ValidateAssignmentAsync(string roleCode, Guid departmentId, CancellationToken ct);
}
