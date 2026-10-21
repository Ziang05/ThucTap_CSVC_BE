using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using CSVC.Core.Common;

namespace CSVC.UseCases.Areas.DTOs;

public abstract class AreaWriteRequest
{
    private string _name = "";
    [Required, StringLength(200)]
    public string Name { get => _name; init => _name = TextNormalization.Name(value ?? ""); }
    [StringLength(100)] public string? TypeLabel { get; init; }
    [StringLength(4000)] public string? Description { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateAreaRequest : AreaWriteRequest
{
    public Guid? ParentId { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdateAreaRequest : AreaWriteRequest
{
    private Guid? _parentId;
    private bool _parentSpecified;
    public Guid? ParentId { get => _parentId; init { _parentId = value; _parentSpecified = true; } }
    // A missing JSON property keeps the current parent; explicit null moves the node to a root.
    public bool HasParentChange() => _parentSpecified;
}

public enum AreaSortBy { Name, Code }
public enum AreaSortDirection { Asc, Desc }

public sealed class AreaListQuery
{
    [StringLength(200)] public string? Search { get; init; }
    [EnumDataType(typeof(EntityStatus))] public EntityStatus? Status { get; init; }
    [EnumDataType(typeof(EntityStatus))] public EntityStatus? EffectiveStatus { get; init; }
    [EnumDataType(typeof(AreaSortBy))] public AreaSortBy SortBy { get; init; }
    [EnumDataType(typeof(AreaSortDirection))] public AreaSortDirection SortDirection { get; init; }
    [Range(1, 1_000_000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
}

public sealed class AreaHistoryQuery
{
    public bool IncludeDescendants { get; init; }
    [Range(1, 1_000_000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
}
