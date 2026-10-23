using CSVC.Core.Common;

namespace CSVC.Core.Suppliers;

public sealed class Supplier
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string DisplayCode { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public string? TaxCode { get; set; }

    public string? Address { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string? Notes { get; set; }

    public EntityStatus Status { get; set; } = EntityStatus.Active;
}
