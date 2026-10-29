using System.Text.Json;
using System.Text.Json.Serialization;
using CSVC.Core.Common;

namespace CSVC.UseCases.Manufacturers.DTOs;

public sealed class ManufacturerListItemDto
{
    public string DisplayCode { get; init; } = "";
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public EntityStatus Status { get; init; }
    public int? DeviceCount => null;
}

public sealed class ManufacturerDetailDto
{
    public string DisplayCode { get; init; } = "";
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public EntityStatus Status { get; init; }
    public int? DeviceCount => null;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Notes { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Country { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Website { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ShortName { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ContactInfo { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Version { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ManufacturerManagementDto? Management { get; set; }
}

public sealed class ManufacturerManagementDto
{
    public string? CreatedByAccountCode { get; init; }
    public string? CreatedByFullName { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public string? UpdatedByAccountCode { get; init; }
    public string? UpdatedByFullName { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}

public sealed class ManufacturerLockImpactDto
{
    public string DisplayCode { get; init; } = "";
    public EntityStatus Status { get; init; }
    public int? DeviceCount { get; init; }
    public bool LinkCountsComplete => DeviceCount.HasValue;
    public string Warning => "Locking prevents new manufacturer assignments; existing device links are preserved.";
}

public class ManufacturerHistoryListItemDto
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

public sealed class ManufacturerHistoryDetailDto : ManufacturerHistoryListItemDto
{
    public string? ReasonCode { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Guid? RequestId { get; set; }
    public IReadOnlyList<string>? ChangedFields { get; set; }
    public IReadOnlyDictionary<string, JsonElement>? BeforeData { get; set; }
    public IReadOnlyDictionary<string, JsonElement>? AfterData { get; set; }
}
