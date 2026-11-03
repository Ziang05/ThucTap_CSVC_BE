namespace CSVC.Core.Settings;

public sealed class EquipmentSettings
{
    public int Id { get; set; } = 1;
    public Guid? EquipmentDepartmentId { get; set; }
    public int Version { get; set; }
    public Guid? UpdatedByAccountId { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public sealed class EquipmentSettingsHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequestId { get; set; }
    public Guid ActorAccountId { get; set; }
    public Guid? PreviousDepartmentId { get; set; }
    public Guid DepartmentId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
