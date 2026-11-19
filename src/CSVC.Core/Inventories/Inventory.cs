namespace CSVC.Core.Inventories;

public static class InventoryStates
{
    public const string Pending = "PENDING_APPROVAL", Preparing = "PREPARING", InProgress = "IN_PROGRESS",
        Completed = "COMPLETED", Canceled = "CANCELED", Draft = "DRAFT", Published = "PUBLISHED", Rejected = "REJECTED";
}

public sealed class InventorySlip
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string DisplayCode { get; set; }
    public Guid CreateRequestId { get; set; }
    public Guid DepartmentId { get; set; }
    public required string DepartmentCodeSnapshot { get; set; }
    public required string DepartmentNameSnapshot { get; set; }
    public string Status { get; set; } = InventoryStates.Pending;
    public Guid CreatedByAccountId { get; set; }
    public required string CreatorCodeSnapshot { get; set; }
    public required string CreatorNameSnapshot { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? ApprovedByAccountId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public Guid? StartedByAccountId { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public Guid? CompletedByAccountId { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public Guid? CanceledByAccountId { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public string? CancelReason { get; set; }
    public string? Notes { get; set; }
    public int Version { get; set; }
    public List<InventoryPlanRevision> Revisions { get; set; } = [];
}

public sealed class InventoryPlanRevision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SlipId { get; set; }
    public int RevisionNo { get; set; }
    public string Status { get; set; } = InventoryStates.Draft;
    public DateTimeOffset PlannedStartAt { get; set; }
    public DateTimeOffset? PlannedEndAt { get; set; }
    public int? NoticeDaysSnapshot { get; set; }
    public string? ChangeReason { get; set; }
    public string? DiffData { get; set; }
    public Guid ProposedByAccountId { get; set; }
    public DateTimeOffset ProposedAt { get; set; }
    public Guid? ApprovedByAccountId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public Guid? RejectedByAccountId { get; set; }
    public DateTimeOffset? RejectedAt { get; set; }
    public string? RejectionReason { get; set; }
    public int Version { get; set; }
    public List<InventoryPlanItem> Items { get; set; } = [];
    public List<InventoryRevisionAssignee> Assignees { get; set; } = [];
}

public sealed class InventoryPlanItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RevisionId { get; set; }
    public Guid DeviceId { get; set; }
    public required string DeviceCodeSnapshot { get; set; }
    public required string DeviceNameSnapshot { get; set; }
    public string? SerialSnapshot { get; set; }
    public int QuantitySnapshot { get; set; }
    public Guid DepartmentIdSnapshot { get; set; }
    public Guid? AreaIdSnapshot { get; set; }
    public string? AreaPathSnapshot { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public InventoryResult? Result { get; set; }
}

public sealed class InventoryRevisionAssignee
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RevisionId { get; set; }
    public Guid AccountId { get; set; }
    public required string AccountCodeSnapshot { get; set; }
    public required string NameSnapshot { get; set; }
    public Guid AssignedByAccountId { get; set; }
    public DateTimeOffset AssignedAt { get; set; }
}

public sealed class InventoryResult
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PlanItemId { get; set; }
    public bool Exists { get; set; }
    public Guid CheckedByAccountId { get; set; }
    public required string CheckerCodeSnapshot { get; set; }
    public required string CheckerNameSnapshot { get; set; }
    public DateTimeOffset CheckedAt { get; set; }
    public Guid InitialRequestId { get; set; }
    public Guid? LastCorrectedByAccountId { get; set; }
    public DateTimeOffset? LastCorrectedAt { get; set; }
    public int Version { get; set; }
}

public sealed class InventoryItemHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ResultId { get; set; }
    public required string ActionCode { get; set; }
    public Guid RequestId { get; set; }
    public required string RequestHash { get; set; }
    public required string ResponseData { get; set; }
    public bool? BeforeExists { get; set; }
    public bool AfterExists { get; set; }
    public int? VersionBefore { get; set; }
    public int VersionAfter { get; set; }
    public Guid ActorAccountId { get; set; }
    public required string ActorNameSnapshot { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string? Reason { get; set; }
}

public sealed class InventoryHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SlipId { get; set; }
    public Guid RequestId { get; set; }
    public required string RequestHash { get; set; }
    public required string ResponseData { get; set; }
    public required string TargetKey { get; set; }
    public required string ActionCode { get; set; }
    public string Outcome { get; set; } = "SUCCESS";
    public Guid? ActorAccountId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string? ReasonCode { get; set; }
    public string? BeforeData { get; set; }
    public string? AfterData { get; set; }
}

public sealed class InventoryNoticePolicy
{
    public short Id { get; set; } = 1;
    public int MinimumNoticeDays { get; set; } = 3;
    public int Version { get; set; }
    public Guid? UpdatedByAccountId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class InventoryNoticePolicyHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public short PolicyId { get; set; } = 1;
    public Guid RequestId { get; set; }
    public required string RequestHash { get; set; }
    public required string ResponseData { get; set; }
    public int PreviousDays { get; set; }
    public int NewDays { get; set; }
    public Guid ChangedByAccountId { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
    public required string Reason { get; set; }
}

public sealed class InventoryNotification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string EventKey { get; set; }
    public Guid SlipId { get; set; }
    public Guid RevisionId { get; set; }
    public Guid RecipientAccountId { get; set; }
    public required string EventType { get; set; }
    public required string PayloadSnapshot { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
    // In-app delivery is committed atomically. No external channel is configured in this project.
    public string DeliveryStatus { get; set; } = "SENT";
    public int Attempts { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public string? LastErrorCode { get; set; }
}
