using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using CSVC.Core.Common;
using CSVC.UseCases.History.DTOs;

namespace CSVC.UseCases.Manufacturers.DTOs;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class ManufacturerWriteRequest : IValidatableObject
{
    private string _name = "";
    [Required, StringLength(200)]
    public string Name { get => _name; init => _name = TextNormalization.Name(value ?? ""); }
    [StringLength(4000)] public string? Description { get; init; }
    [StringLength(4000)] public string? Notes { get; init; }
    private string? _country;
    private string? _website;
    private string? _shortName;
    private string? _contactInfo;
    [StringLength(120)]
    public string? Country { get => _country; init => _country = string.IsNullOrWhiteSpace(value) ? null : TextNormalization.Name(value); }
    [StringLength(2048)]
    public string? Website { get => _website; init => _website = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); }
    [StringLength(120)]
    public string? ShortName { get => _shortName; init => _shortName = string.IsNullOrWhiteSpace(value) ? null : TextNormalization.Name(value); }
    [StringLength(4000)]
    public string? ContactInfo { get => _contactInfo; init => _contactInfo = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Website is not null && !IsValidWebsite(Website))
            yield return new ValidationResult("Website must be an absolute HTTP or HTTPS URL without embedded credentials.", [nameof(Website)]);
    }

    internal static bool IsValidWebsite(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.IsWellFormedOriginalString()
            && uri.Scheme is "http" or "https" && !string.IsNullOrWhiteSpace(uri.Host) && string.IsNullOrEmpty(uri.UserInfo);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateManufacturerRequest : ManufacturerWriteRequest
{
    private string? _displayCode;
    [StringLength(64), RegularExpression(BusinessCode.Pattern)]
    public string? DisplayCode { get => _displayCode; init => _displayCode = BusinessCode.Normalize(value); }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdateManufacturerRequest : ManufacturerWriteRequest
{
    // Clients can supply the version returned by detail to reject stale edits without a schema change.
    [RegularExpression("^[A-Fa-f0-9]{64}$")] public string? Version { get; init; }
}

public enum ManufacturerSortBy { Name, DisplayCode, Status }
public enum ManufacturerSortDirection { Asc, Desc }

public sealed class ManufacturerListQuery
{
    [StringLength(200)] public string? Search { get; init; }
    [EnumDataType(typeof(EntityStatus))] public EntityStatus? Status { get; init; }
    [EnumDataType(typeof(ManufacturerSortBy))] public ManufacturerSortBy SortBy { get; init; }
    [EnumDataType(typeof(ManufacturerSortDirection))] public ManufacturerSortDirection SortDirection { get; init; }
    [Range(1, 1_000_000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
}

public sealed class ManufacturerHistoryQuery : IValidatableObject
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
