using System.ComponentModel.DataAnnotations;
using CSVC.Core.Inventories;
using CSVC.UseCases.Common;

namespace CSVC.UseCases.Inventories;

public class InventoryCommand
{
    public Guid RequestId { get; set; }
    [Required, Range(0, int.MaxValue)] public int? ExpectedVersion { get; set; }
}
public sealed class CreateInventoryRequest
{
    public Guid RequestId { get; set; }
    public Guid DepartmentId { get; set; }
    public DateTimeOffset PlannedStartAt { get; set; }
    public DateTimeOffset? PlannedEndAt { get; set; }
    [MaxLength(2000)] public List<Guid>? DeviceIds { get; set; }
    [Required, MaxLength(100)] public List<Guid> AssigneeAccountIds { get; set; } = [];
    [MaxLength(4000)] public string? Notes { get; set; }
}
public sealed class UpdateInventoryPlanRequest : InventoryCommand
{
    public Guid DepartmentId { get; set; }
    public DateTimeOffset PlannedStartAt { get; set; }
    public DateTimeOffset? PlannedEndAt { get; set; }
    [MaxLength(2000)] public List<Guid>? DeviceIds { get; set; }
    [Required, MaxLength(100)] public List<Guid> AssigneeAccountIds { get; set; } = [];
    [MaxLength(4000)] public string? Notes { get; set; }
    [MaxLength(2000)] public string? Reason { get; set; }
}
public sealed class InventoryReasonRequest : InventoryCommand
{
    [Required, MaxLength(2000)] public string Reason { get; set; } = "";
}
public sealed class RecordInventoryResultRequest
{
    public Guid RequestId { get; set; }
    [Required] public bool? Exists { get; set; }
}
public sealed class CorrectInventoryResultRequest : InventoryCommand
{
    [Required] public bool? NewExists { get; set; }
    [Required, MaxLength(2000)] public string Reason { get; set; } = "";
}
public sealed class UpdateInventoryPolicyRequest : InventoryCommand
{
    [Range(1, 365)] public int MinimumNoticeDays { get; set; }
    [Required, MaxLength(2000)] public string Reason { get; set; } = "";
}
public sealed class InventoryListQuery
{
    [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 20;
    [MaxLength(200)] public string? Search { get; set; }
    public Guid? DepartmentId { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
}

public sealed record InventoryPersonDto(Guid AccountId, string AccountCode, string Name);
public sealed record InventoryPlanItemDto(Guid Id, Guid DeviceId, string DeviceCode, string DeviceName,
    string? Serial, int Quantity, Guid DepartmentId, Guid? AreaId, string? AreaPath, DateTimeOffset CapturedAt);
public sealed record InventoryPlanDto(Guid SlipId, string DisplayCode, string Status, Guid DepartmentId,
    string DepartmentCode, string DepartmentName, InventoryPersonDto Creator, DateTimeOffset CreatedAt,
    Guid RevisionId, int RevisionNo, string RevisionStatus, DateTimeOffset PlannedStartAt,
    DateTimeOffset? PlannedEndAt, DateTimeOffset? PublishedAt, int? NoticeDays, int Version,
    string? Notes, IReadOnlyList<InventoryPlanItemDto> Items);
public sealed record InventoryResultDto(Guid ItemId, bool Exists, InventoryPersonDto CheckedBy,
    DateTimeOffset CheckedAt, Guid? LastCorrectedByAccountId, DateTimeOffset? LastCorrectedAt, int Version);
public sealed record InventoryInspectorDto(InventoryPersonDto Person, int CheckedItems, DateTimeOffset FirstCheckedAt);
public sealed record InventoryProgressDto(int Total, int Checked, int Exists, int NotExists, int Unchecked,
    IReadOnlyList<InventoryInspectorDto> Inspectors);
public sealed record InventoryRevisionDto(Guid Id, int RevisionNo, string Status, DateTimeOffset PlannedStartAt,
    DateTimeOffset? PlannedEndAt, int Version, string? Reason, string? DiffData,
    IReadOnlyList<InventoryPersonDto> Assignees, IReadOnlyList<InventoryPlanItemDto> Items);
public sealed record InventorySlipDto(InventoryPlanDto Plan, IReadOnlyList<InventoryRevisionDto> Revisions,
    InventoryProgressDto Progress, IReadOnlyList<InventoryResultDto> Results, DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt, string? CancelReason);
public sealed record InventorySummaryDto(Guid Id, string DisplayCode, string Status, string DepartmentCode,
    string DepartmentName, DateTimeOffset PlannedStartAt, int TotalItems, int Version);
public sealed record InventoryReportDto(string DocumentStatus, InventoryPlanDto Plan, InventoryProgressDto Progress,
    IReadOnlyList<InventoryResultDto> Results, IReadOnlyList<InventoryItemHistoryDto> Corrections,
    DateTimeOffset? CompletedAt, Guid? CompletedByAccountId);
public sealed record InventoryHistoryDto(Guid Id, string TargetKey, string ActionCode, Guid? ActorAccountId,
    DateTimeOffset OccurredAt, string? ReasonCode, string? BeforeData, string? AfterData);
public sealed record InventoryItemHistoryDto(Guid Id, Guid ResultId, string ActionCode, bool? BeforeExists,
    bool AfterExists, int? VersionBefore, int VersionAfter, Guid ActorAccountId, string ActorName,
    DateTimeOffset OccurredAt, string? Reason);
public sealed record InventoryPolicyDto(int MinimumNoticeDays, int Version, Guid? UpdatedByAccountId, DateTimeOffset UpdatedAt);
public sealed record InventoryPolicyHistoryDto(int PreviousDays, int NewDays, Guid ChangedByAccountId, DateTimeOffset ChangedAt, string Reason);
public sealed record InventoryNotificationDto(Guid Id, Guid SlipId, Guid RevisionId, string EventType,
    string PayloadSnapshot, DateTimeOffset PublishedAt, DateTimeOffset? ReadAt);

public interface IInventoryService
{
    Task<PagedResult<InventoryDeviceOption>> DeviceOptionsAsync(Guid departmentId, string? search, int page, int size, CancellationToken ct);
    Task<PagedResult<InventoryAssigneeOption>> AssigneeOptionsAsync(string? search, int page, int size, CancellationToken ct);
    Task<PagedResult<InventorySummaryDto>> ListAsync(InventoryListQuery query, CancellationToken ct);
    Task<object> GetAsync(Guid id, CancellationToken ct);
    Task<InventoryPlanDto> PlanAsync(Guid id, CancellationToken ct);
    Task<InventorySlipDto> CreateAsync(CreateInventoryRequest request, CancellationToken ct);
    Task<InventorySlipDto> UpdateAsync(Guid id, UpdateInventoryPlanRequest request, CancellationToken ct);
    Task<InventorySlipDto> ProposeAsync(Guid id, UpdateInventoryPlanRequest request, CancellationToken ct);
    Task<InventorySlipDto> PublishAsync(Guid id, Guid revisionId, InventoryCommand request, CancellationToken ct);
    Task<InventorySlipDto> RejectRevisionAsync(Guid id, Guid revisionId, InventoryReasonRequest request, CancellationToken ct);
    Task<InventorySlipDto> CancelAsync(Guid id, InventoryReasonRequest request, CancellationToken ct);
    Task<InventorySlipDto> StartAsync(Guid id, InventoryCommand request, CancellationToken ct);
    Task<InventorySlipDto> CompleteAsync(Guid id, InventoryCommand request, CancellationToken ct);
    Task<InventoryResultDto> RecordAsync(Guid id, Guid itemId, RecordInventoryResultRequest request, CancellationToken ct);
    Task<InventoryResultDto> CorrectAsync(Guid id, Guid itemId, CorrectInventoryResultRequest request, CancellationToken ct);
    Task<InventoryProgressDto> ProgressAsync(Guid id, CancellationToken ct);
    Task<PagedResult<InventoryHistoryDto>> HistoryAsync(Guid id, int page, int pageSize, CancellationToken ct);
    Task<PagedResult<InventoryItemHistoryDto>> ItemHistoryAsync(Guid id, Guid itemId, int page, int pageSize, CancellationToken ct);
    Task<InventoryReportDto> ReportAsync(Guid id, CancellationToken ct);
    Task<byte[]> ExportAsync(Guid id, CancellationToken ct);
    Task<InventoryPolicyDto> PolicyAsync(CancellationToken ct);
    Task<InventoryPolicyDto> UpdatePolicyAsync(UpdateInventoryPolicyRequest request, CancellationToken ct);
    Task<PagedResult<InventoryPolicyHistoryDto>> PolicyHistoryAsync(int page, int pageSize, CancellationToken ct);
    Task<PagedResult<InventoryNotificationDto>> NotificationsAsync(int page, int pageSize, CancellationToken ct);
    Task MarkReadAsync(Guid id, CancellationToken ct);
    Task<int> RemindAsync(CancellationToken ct);
}

public sealed record InventoryRequestReceipt(Guid? ActorId, string Action, string Hash, string Response);

public interface IEquipmentInventoryRepository
{
    Task<PagedResult<InventoryDeviceOption>> DeviceOptionsAsync(Guid departmentId, string? search, int page, int size, CancellationToken ct);
    Task<PagedResult<InventoryAssigneeOption>> AssigneeOptionsAsync(Guid departmentId, string? search, int page, int size, CancellationToken ct);
    Task<InventorySlip?> LoadAsync(Guid id, bool tracked, CancellationToken ct);
    Task<PagedResult<InventorySummaryDto>> ListAsync(InventoryListQuery query, Guid? departmentScope, CancellationToken ct);
    Task<InventoryRequestReceipt?> RequestAsync(Guid requestId, CancellationToken ct);
    Task<IReadOnlyList<Guid>> RecipientsAsync(Guid departmentId, CancellationToken ct);
    Task<bool> OverlapsAsync(Guid slipId, Guid departmentId, DateTimeOffset start, DateTimeOffset? end, CancellationToken ct);
    Task<PagedResult<InventoryHistoryDto>> HistoryAsync(Guid id, int page, int size, CancellationToken ct);
    Task<PagedResult<InventoryItemHistoryDto>> ItemHistoryAsync(Guid itemId, int page, int size, CancellationToken ct);
    Task<IReadOnlyList<InventoryItemHistoryDto>> CorrectionsAsync(Guid revisionId, CancellationToken ct);
    Task<IReadOnlyList<InventorySlip>> DueAsync(DateTimeOffset now, CancellationToken ct);
    Task<bool> NoticeExistsAsync(string key, CancellationToken ct);
    void ReplaceDraft(InventoryPlanRevision revision, IReadOnlyList<InventoryPlanItem> items, IReadOnlyList<InventoryRevisionAssignee> assignees);
    Task<PagedResult<InventoryNotificationDto>> InboxAsync(Guid accountId, int page, int size, CancellationToken ct);
    Task<PagedResult<InventoryPolicyHistoryDto>> PolicyHistoryAsync(int page, int size, CancellationToken ct);
}
