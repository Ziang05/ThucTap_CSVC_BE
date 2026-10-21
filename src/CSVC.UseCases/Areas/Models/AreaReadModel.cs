namespace CSVC.UseCases.Areas.Models;

// Scalar/array projection from a recursive PostgreSQL query; never a tracked entity.
public sealed class AreaReadModel
{
    public Guid Id { get; init; }
    public string Code { get; init; } = "";
    public Guid? ParentId { get; init; }
    public string Name { get; init; } = "";
    public string NormalizedName { get; init; } = "";
    public string? TypeLabel { get; init; }
    public string? Description { get; init; }
    public bool IsLocked { get; init; }
    public bool IsEffectivelyLocked { get; init; }
    public int Depth { get; init; }
    public int ChildCount { get; init; }
    public Guid[] PathIds { get; init; } = [];
    public string[] PathNames { get; init; } = [];
}
