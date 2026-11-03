using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace CSVC.UseCases.Settings;

public sealed record EquipmentAccountMismatchDto(Guid AccountId, string AccountCode, string FullName, string RoleCode, string DepartmentName);
public sealed record EquipmentSettingsDto(bool IsConfigured, string? DepartmentCode, string? DepartmentName, int Version,
    DateTimeOffset? UpdatedAt, IReadOnlyList<EquipmentAccountMismatchDto> MismatchedAccounts);
public sealed record EquipmentWorkflowContextDto(bool IsConfigured, string? EquipmentDepartmentCode, bool EquipmentAccountMatchesDepartment,
    Guid ActorDepartmentId, string ActorDepartmentCode);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdateEquipmentDepartmentRequest
{
    [Required, StringLength(32)] public string DepartmentCode { get; init; } = "";
    [Range(0, int.MaxValue)] public int Version { get; init; }
}

public interface IEquipmentSettingsService
{
    Task<EquipmentSettingsDto> GetAsync(CancellationToken ct);
    Task UpdateAsync(UpdateEquipmentDepartmentRequest request, CancellationToken ct);
    Task<EquipmentWorkflowContextDto> ContextAsync(CancellationToken ct);
}
