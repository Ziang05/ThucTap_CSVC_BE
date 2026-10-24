using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using CSVC.Core.Common;
using CSVC.UseCases.History.DTOs;

namespace CSVC.UseCases.DeviceCategories.DTOs;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class CategoryWriteRequest
{
    private string _name = "";
    [Required, StringLength(200)]
    public string Name { get => _name; init => _name = TextNormalization.Name(value ?? ""); }
    [StringLength(4000)] public string? Description { get; init; }
    [StringLength(4000)] public string? Notes { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateCategoryRequest : CategoryWriteRequest
{
    private string? _displayCode;
    [StringLength(64), RegularExpression(BusinessCode.Pattern)]
    public string? DisplayCode { get => _displayCode; init => _displayCode = BusinessCode.Normalize(value); }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdateCategoryRequest : CategoryWriteRequest
{
    // Clients can supply the version returned by detail to reject stale edits without a schema change.
    [RegularExpression("^[A-Fa-f0-9]{64}$")] public string? Version { get; init; }
}

public enum CategorySortBy { Name, DisplayCode, Status }
public enum CategorySortDirection { Asc, Desc }

public sealed class CategoryListQuery
{
    [StringLength(200)] public string? Search { get; init; }
    [EnumDataType(typeof(EntityStatus))] public EntityStatus? Status { get; init; }
    [EnumDataType(typeof(CategorySortBy))] public CategorySortBy SortBy { get; init; }
    [EnumDataType(typeof(CategorySortDirection))] public CategorySortDirection SortDirection { get; init; }
    [Range(1, 1_000_000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
}

public sealed class CategoryHistoryQuery : IValidatableObject
{
    private DateTimeOffset? _from;
    private DateTimeOffset? _to;
    public DateTimeOffset? From { get => _from; init => _from = value?.ToUniversalTime(); }
    public DateTimeOffset? To { get => _to; init => _to = value?.ToUniversalTime(); }
    [StringLength(200)] public string? Search { get; init; }
    [StringLength(64)] public string? DisplayCode { get; init; }
    [StringLength(32)] public string? ActorAccountCode { get; init; }
    [StringLength(64)] public string? ActionCode { get; init; }
    [EnumDataType(typeof(AuditOutcome))] public AuditOutcome? Outcome { get; init; }
    [EnumDataType(typeof(HistorySortBy))] public HistorySortBy SortBy { get; init; } = HistorySortBy.OccurredAt;
    [EnumDataType(typeof(HistorySortDirection))] public HistorySortDirection SortDirection { get; init; } = HistorySortDirection.Desc;
    [Range(1, 1_000_000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (From > To) yield return new ValidationResult("From must not be later than To.", [nameof(From), nameof(To)]);
    }
}
