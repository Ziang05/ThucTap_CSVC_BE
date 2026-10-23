using CSVC.Core.Common;

namespace CSVC.Core.Manufacturers;

public sealed class Manufacturer
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string DisplayCode { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public string? Country { get; set; }

    public string? Website { get; set; }

    public string? ShortName { get; set; }

    public string? ContactInfo { get; set; }

    public string? Notes { get; set; }

    public EntityStatus Status { get; set; } = EntityStatus.Active;
}
