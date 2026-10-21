using System.Text.Json.Serialization;
using CSVC.Core.Common;

namespace CSVC.UseCases.Departments.DTOs;

public sealed class DepartmentListItemDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string? ShortName { get; init; }
    public EntityStatus Status { get; init; }
}

public sealed class DepartmentDetailDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string? ShortName { get; init; }
    public string? Description { get; init; }
    public EntityStatus Status { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DepartmentManagementDto? Management { get; set; }
}

public sealed class DepartmentManagementDto
{
    public Guid? CreatedBy { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public int AccountCount { get; init; }
}

public sealed class DepartmentLockImpactDto
{
    public Guid DepartmentId { get; init; }
    public EntityStatus Status { get; init; }
    public int AccountCount { get; init; }
    // Unknown counts remain null until the corresponding business modules exist.
    public int? DeviceCount { get; init; }
    public int? RequestCount { get; init; }
    public bool LinkCountsComplete => DeviceCount.HasValue && RequestCount.HasValue;
    public string Warning => "Locking prevents new assignments; existing accounts and links are preserved.";
}

public sealed class DepartmentHistoryDto
{
    public Guid Id { get; init; }
    public Guid RequestId { get; init; }
    public Guid? ActorAccountId { get; init; }
    public Guid? DepartmentId { get; init; }
    public string ActionCode { get; init; } = "";
    public AuditOutcome Outcome { get; init; }
    public string? ReasonCode { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public string? BeforeData { get; init; }
    public string? AfterData { get; init; }
}
