using System.Text.Json;
using System.Text.Json.Serialization;
using CSVC.Core.Common;

namespace CSVC.UseCases.DeviceCategories.DTOs;

public record CategoryPathItemDto(string DisplayCode, string Name);

public class CategoryListItemDto
{
    public string DisplayCode { get; init; } = "";
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public EntityStatus Status { get; init; }
    public string? ParentDisplayCode { get; set; }
    public IReadOnlyList<CategoryPathItemDto> Path { get; set; } = [];
    public int? MaintenanceIntervalMonths { get; init; }
    public int? EffectiveMaintenanceIntervalMonths { get; set; }
    public string? MaintenanceIntervalSourceCode { get; set; }
    public int? DeviceCount => null;
}

public sealed class CategoryDetailDto : CategoryListItemDto
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Notes { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Version { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public CategoryManagementDto? Management { get; set; }
}

public sealed class CategoryManagementDto
{
    public string? CreatedByAccountCode { get; init; }
    public string? CreatedByFullName { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public string? UpdatedByAccountCode { get; init; }
    public string? UpdatedByFullName { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}

public sealed class CategoryLockImpactDto
{
    public string DisplayCode { get; init; } = "";
    public EntityStatus Status { get; init; }
    public int? DeviceCount { get; init; }
    public bool LinkCountsComplete => DeviceCount.HasValue;
    public string Warning => "Locking prevents new category assignments; existing device links are preserved.";
}

public class CategoryHistoryListItemDto
{
    public Guid Id { get; init; }
    public string? DisplayCode { get; init; }
    public string? Name { get; init; }
    public string ActionCode { get; init; } = "";
    public AuditOutcome Outcome { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public string? ActorAccountCode { get; init; }
    public string? ActorFullName { get; init; }
}

public sealed class CategoryHistoryDetailDto : CategoryHistoryListItemDto
{
    public string? ReasonCode { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Guid? RequestId { get; set; }
    public IReadOnlyList<string>? ChangedFields { get; set; }
    public IReadOnlyDictionary<string, JsonElement>? BeforeData { get; set; }
    public IReadOnlyDictionary<string, JsonElement>? AfterData { get; set; }
}
