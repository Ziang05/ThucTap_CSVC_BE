using CSVC.Core.Common;

namespace CSVC.Core.Areas;

public sealed class Area
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string Code { get; set; }

    public Guid? ParentId { get; set; }

    public required string Name { get; set; }

    public string? TypeLabel { get; set; }

    public string? Description { get; set; }

    public EntityStatus Status { get; set; } = EntityStatus.Active;
}
