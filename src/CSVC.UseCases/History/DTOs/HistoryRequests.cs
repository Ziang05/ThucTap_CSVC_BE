using System.ComponentModel.DataAnnotations;
using CSVC.Core.Common;

namespace CSVC.UseCases.History.DTOs;

public enum HistorySortBy { OccurredAt, ActionCode, Outcome, ActorAccountCode }
public enum HistorySortDirection { Asc, Desc }

public class HistoryQuery : IValidatableObject
{
    private DateTimeOffset? _from;
    private DateTimeOffset? _to;
    public DateTimeOffset? From { get => _from; init => _from = value?.ToUniversalTime(); }
    public DateTimeOffset? To { get => _to; init => _to = value?.ToUniversalTime(); }
    [StringLength(200)] public string? Search { get; init; }
    [StringLength(64)] public string? ActionCode { get; init; }
    [EnumDataType(typeof(AuditOutcome))] public AuditOutcome? Outcome { get; init; }
    [StringLength(80)] public string? ReasonCode { get; init; }
    public Guid? ActorAccountId { get; init; }
    public Guid? RequestId { get; init; }
    [EnumDataType(typeof(HistorySortBy))] public HistorySortBy SortBy { get; init; } = HistorySortBy.OccurredAt;
    [EnumDataType(typeof(HistorySortDirection))] public HistorySortDirection SortDirection { get; init; } = HistorySortDirection.Desc;
    [Range(1, 1_000_000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;

    protected virtual IEnumerable<(Guid? Value, string Name)> Ids()
        => [(ActorAccountId, nameof(ActorAccountId)), (RequestId, nameof(RequestId))];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (From > To)
            yield return new ValidationResult("From must not be later than To.", [nameof(From), nameof(To)]);
        foreach (var (value, name) in Ids())
            if (value == Guid.Empty) yield return new ValidationResult("Select a non-empty ID.", [name]);
    }
}

public sealed class AccountHistoryQuery : HistoryQuery
{
    public Guid? TargetAccountId { get; init; }
    protected override IEnumerable<(Guid? Value, string Name)> Ids()
        => base.Ids().Append((TargetAccountId, nameof(TargetAccountId)));
}

public sealed class AuthHistoryQuery : HistoryQuery
{
    public Guid? AccountId { get; init; }
    public Guid? SessionId { get; init; }
    [StringLength(320)] public string? EmailSubmitted { get; init; }
    protected override IEnumerable<(Guid? Value, string Name)> Ids()
        => base.Ids().Append((AccountId, nameof(AccountId))).Append((SessionId, nameof(SessionId)));
}

public sealed class DepartmentHistoryQuery : HistoryQuery
{
    public Guid? DepartmentId { get; init; }
    protected override IEnumerable<(Guid? Value, string Name)> Ids()
        => base.Ids().Append((DepartmentId, nameof(DepartmentId)));
}
