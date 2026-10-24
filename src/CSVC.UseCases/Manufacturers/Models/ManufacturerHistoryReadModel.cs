using CSVC.Core.Common;

namespace CSVC.UseCases.Manufacturers.Models;

// Internal query models: UUIDs and raw snapshots are never projected directly to public responses.
public class ManufacturerHistoryReadModel
{
    public Guid Id { get; init; }
    public Guid? ManufacturerId { get; init; }
    public string? DisplayCode { get; init; }
    public string? Name { get; init; }
    public string ActionCode { get; init; } = "";
    public AuditOutcome Outcome { get; init; }
    public string OutcomeCode { get; init; } = "";
    public DateTimeOffset OccurredAt { get; init; }
    public string? ActorAccountCode { get; init; }
    public string? ActorFullName { get; init; }
}

public sealed class ManufacturerHistoryDetailReadModel : ManufacturerHistoryReadModel
{
    public Guid RequestId { get; init; }
    public string? ReasonCode { get; init; }
    public string? ChangedFields { get; init; }
    public string? BeforeData { get; init; }
    public string? AfterData { get; init; }
}
