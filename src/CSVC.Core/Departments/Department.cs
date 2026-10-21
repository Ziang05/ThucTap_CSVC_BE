using CSVC.Core.Common;

namespace CSVC.Core.Departments;

public sealed class Department
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string Code { get; set; }

    public required string Name { get; set; }

    public string? ShortName { get; set; }

    public string? Description { get; set; }

    public EntityStatus Status { get; set; } = EntityStatus.Active;
}
