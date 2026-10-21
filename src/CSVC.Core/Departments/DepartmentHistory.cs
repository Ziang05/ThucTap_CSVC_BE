using CSVC.Core.Common;

namespace CSVC.Core.Departments;

public sealed class DepartmentHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RequestId { get; set; }

    public Guid? ActorAccountId { get; set; }

    public required string ActionCode { get; set; }

    public required AuditOutcome Outcome { get; set; }

    public string? ReasonCode { get; set; }

    public required DateTimeOffset OccurredAt { get; set; }

    public Guid? DepartmentId { get; set; }

    public string? BeforeData { get; set; }

    public string? AfterData { get; set; }
}
