using System.Text.Json.Serialization;
using CSVC.Core.Common;
using CSVC.UseCases.Common;

namespace CSVC.UseCases.Areas.DTOs;

public class AreaListItemDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = "";
    public Guid? ParentId { get; init; }
    public string Name { get; init; } = "";
    public string? TypeLabel { get; init; }
    public EntityStatus Status { get; init; }
    public EntityStatus EffectiveStatus { get; init; }
    public int Depth { get; init; }
    public int ChildCount { get; init; }
    public Guid[] PathIds { get; init; } = [];
    public string[] PathNames { get; init; } = [];
    public string Path => string.Join(" / ", PathNames);
}

public sealed class AreaDetailDto : AreaListItemDto
{
    public string? Description { get; init; }
    public PagedResult<AreaListItemDto> Children { get; set; } = new([], 0, 1, 20);
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AreaManagementDto? Management { get; set; }
}

public sealed class AreaManagementDto
{
    public Guid? CreatedBy { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public int? DirectDeviceCount { get; init; }
    public int? SubtreeDeviceCount { get; init; }
}

public sealed class AreaLockImpactDto
{
    public Guid AreaId { get; init; }
    public EntityStatus Status { get; init; }
    public EntityStatus EffectiveStatus { get; init; }
    public int ChildCount { get; init; }
    public int DescendantCount { get; init; }
    public int? DirectDeviceCount { get; init; }
    public int? SubtreeDeviceCount { get; init; }
    public int? RequestCount { get; init; }
    public bool LinkCountsComplete => DirectDeviceCount.HasValue && SubtreeDeviceCount.HasValue && RequestCount.HasValue;
    public string Warning => "Locking prevents new use in this branch; child states and existing links are preserved. Request counts are unavailable.";
}
