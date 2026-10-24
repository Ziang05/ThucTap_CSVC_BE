using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using CSVC.Core.Common;
using CSVC.UseCases.History.DTOs;

namespace CSVC.UseCases.Suppliers.DTOs;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class SupplierWriteRequest
{
    private string _name = "";
    [Required, StringLength(200)]
    public string Name { get => _name; init => _name = TextNormalization.Name(value ?? ""); }
    [StringLength(4000)] public string? Description { get; init; }
    [StringLength(4000)] public string? Notes { get; init; }
    private string? _taxCode;
    private string? _email;
    private string? _phone;
    [StringLength(50), RegularExpression("^[A-Z0-9]+(?:[-.][A-Z0-9]+)*$")]
    public string? TaxCode { get => _taxCode; init => _taxCode = string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant(); }
    [StringLength(4000)] public string? Address { get; init; }
    [StringLength(32), RegularExpression(@"^\+?[0-9](?:[0-9 ().-]*[0-9])?$")]
    public string? Phone { get => _phone; init => _phone = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); }
    [StringLength(320), EmailAddress]
    public string? Email { get => _email; init => _email = string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant(); }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateSupplierRequest : SupplierWriteRequest
{
    private string? _displayCode;
    [StringLength(64), RegularExpression(BusinessCode.Pattern)]
    public string? DisplayCode { get => _displayCode; init => _displayCode = BusinessCode.Normalize(value); }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdateSupplierRequest : SupplierWriteRequest
{
    // Clients can supply the version returned by detail to reject stale edits without a schema change.
    [RegularExpression("^[A-Fa-f0-9]{64}$")] public string? Version { get; init; }
}

public enum SupplierSortBy { Name, DisplayCode, Status }
public enum SupplierSortDirection { Asc, Desc }

public sealed class SupplierListQuery
{
    [StringLength(200)] public string? Search { get; init; }
    [EnumDataType(typeof(EntityStatus))] public EntityStatus? Status { get; init; }
    [EnumDataType(typeof(SupplierSortBy))] public SupplierSortBy SortBy { get; init; }
    [EnumDataType(typeof(SupplierSortDirection))] public SupplierSortDirection SortDirection { get; init; }
    [Range(1, 1_000_000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
}

public sealed class SupplierHistoryQuery : IValidatableObject
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
