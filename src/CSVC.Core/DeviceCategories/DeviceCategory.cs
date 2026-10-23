using CSVC.Core.Common;

namespace CSVC.Core.DeviceCategories;

public sealed class DeviceCategory
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string DisplayCode { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public string? Notes { get; set; }

    public EntityStatus Status { get; set; } = EntityStatus.Active;
}
