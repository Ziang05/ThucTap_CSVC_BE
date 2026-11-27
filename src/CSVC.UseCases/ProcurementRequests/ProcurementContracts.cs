using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using CSVC.Core.ProcurementRequests;
using CSVC.UseCases.Common;

namespace CSVC.UseCases.ProcurementRequests;

public enum PmmsEventAction { Create, Update, Cancel }

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PmmsSimulatedEvent
{
    public Guid EventId { get; init; }
    [Required, StringLength(64)] public string ExternalCode { get; init; } = "";
    [Range(1, int.MaxValue)] public int SourceVersion { get; init; }
    [EnumDataType(typeof(PmmsEventAction))] public PmmsEventAction Action { get; init; }
    public PmmsRequestPayload? Request { get; init; }
    [StringLength(1000)] public string? Reason { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PmmsRequestPayload
{
    [EnumDataType(typeof(ProcurementRequestType))] public ProcurementRequestType RequestType { get; init; }
    [Required, StringLength(200)] public string Title { get; init; } = "";
    [Required, StringLength(32)] public string RequestingDepartmentCode { get; init; } = "";
    [StringLength(200)] public string? Contact { get; init; }
    [StringLength(100)] public string? ContractReference { get; init; }
    [StringLength(100)] public string? SupplierReference { get; init; }
    [StringLength(4000)] public string? Notes { get; init; }
    [Required, StringLength(64)] public string SourceStatus { get; init; } = "Submitted";
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    [Required, MinLength(1), MaxLength(100)] public List<PmmsRequestLine> Lines { get; init; } = [];
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PmmsRequestLine
{
    [Required, StringLength(64)] public string SourceLineCode { get; init; } = "";
    [EnumDataType(typeof(ProcurementLineKind))] public ProcurementLineKind Kind { get; init; }
    [Required, StringLength(1000)] public string Description { get; init; } = "";
    [Range(typeof(decimal), "0.001", "1000000")] public decimal Quantity { get; init; } = 1;
    [Required, StringLength(32)] public string Unit { get; init; } = "Cai";
    [StringLength(64)] public string? CategoryCode { get; init; }
    [StringLength(32)] public string? DeviceCode { get; init; }
    [StringLength(64)] public string? DamageReportCode { get; init; }
    [EnumDataType(typeof(EquipmentSpecialization))] public EquipmentSpecialization Specialization { get; init; }
}

public sealed class ProcurementQuery
{
    [StringLength(200)] public string? Search { get; init; }
    [EnumDataType(typeof(ProcurementRequestType))] public ProcurementRequestType? RequestType { get; init; }
    [EnumDataType(typeof(ProcurementRequestStatus))] public ProcurementRequestStatus? Status { get; init; }
    public bool? RequiresReview { get; init; }
    public bool AssignedToMe { get; init; }
    public bool Unassigned { get; init; }
    [Range(1, 1000000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
}
public sealed record ProcurementSummary(string DisplayCode, string Source, string ExternalCode, ProcurementRequestType RequestType,
    string Title, string RequestingDepartmentCode, ProcurementRequestStatus Status, bool RequiresReview, int SourceVersion, int Version, DateTimeOffset ReceivedAt)
{
    public string? AssigneeAccountCode { get; init; }
    public string? AssigneeName { get; init; }
}
public sealed record ProcurementLineDto(string SourceLineCode, ProcurementLineKind Kind, string Description, decimal Quantity,
    string Unit, string? CategoryCode, string? DeviceCode, string? DamageReportCode, EquipmentSpecialization Specialization);
public sealed record RequestDeviceBinding(string SourceLineCode, string DeviceCode, string? CategoryCode = null,
    string? ExecutionMethod = null, string? DamageReportCode = null)
{
    public string? SpecializedHandlingReason { get; init; }
}
public sealed record RequestDocumentDto(RequestDocumentKind Kind, string DocumentCode, string Status, bool Executed, bool Open,
    IReadOnlyList<RequestDeviceBinding> Bindings, IReadOnlyList<RequestMaterialBinding>? MaterialBindings = null);
public sealed record RequestMaterialBinding(string? SourceLineCode, string MaterialCode, decimal Quantity, string Unit);
public sealed record ProcurementEventDto(Guid? ExternalEventId, int? SourceVersion, string Action, string Outcome, string? Reason,
    DateTimeOffset OccurredAt, string? ActorAccountCode, string? SourcePayload);
public sealed record ProcurementDetail(ProcurementSummary Summary, bool IsSimulated, string? Contact, string? ContractReference,
    string? SupplierReference, string SourceStatus, DateTimeOffset SourceCreatedAt, DateTimeOffset SourceUpdatedAt,
    string? Notes, string? MappingIssue, string? PendingSourcePayload, string? Result, string? ResultReason, DateTimeOffset? ClosedAt,
    IReadOnlyList<ProcurementLineDto> Lines, IReadOnlyList<RequestDocumentDto> Documents, bool CanCancel)
{
    public RequestAssignmentDto? Assignment { get; init; }
    public bool CanExecute { get; init; }
    public bool CanAssign { get; init; }
    public ServiceEvidenceDto? ServiceEvidence { get; init; }
    public ResultAcceptanceDto? Acceptance { get; init; }
    public bool RequiresResultAcceptance { get; init; }
    public bool CanAcceptResults { get; init; }
    public IReadOnlyList<RepairResultDto> RepairResults { get; init; } = [];
}
public sealed record RequestAssigneeDto(string AccountCode, string FullName);
public sealed record RequestAssignmentDto(string AccountCode, string FullName, string AssignedByAccountCode, DateTimeOffset AssignedAt);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AssignSourceRequest
{
    public Guid RequestId { get; init; }
    [Range(1, int.MaxValue)] public int Version { get; init; }
    [StringLength(32)] public string? AssigneeAccountCode { get; init; }
    [StringLength(1000)] public string? Reason { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class StartSourceRepair
{
    public Guid RequestId { get; init; }
    [Range(1, int.MaxValue)] public int Version { get; init; }
    [Required, MinLength(1), MaxLength(100)] public List<SourceRepairPlan> Items { get; init; } = [];
}
public sealed class SourceRepairPlan
{
    [Required, StringLength(64)] public string SourceLineCode { get; init; } = "";
    [EnumDataType(typeof(CSVC.Core.ServiceOrders.ExecutionMethod))] public CSVC.Core.ServiceOrders.ExecutionMethod ExecutionMethod { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Guid? DamageSessionId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool OutsideDamageSession { get; init; }
}
public sealed record RequestWorkloadDto(string? AccountCode, string FullName, int Open, int Waiting, int Completed, int Cancelled);
public sealed record RepairResultDto(string DeviceCode, string? Outcome, string? Description, DateTimeOffset? RecordedAt);
public enum ServiceEvidenceOutcome { Completed, Failed, NotExecuted }
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RecordServiceEvidence
{
    public Guid RequestId { get; init; }
    [Range(1, int.MaxValue)] public int Version { get; init; }
    [Required, MinLength(1), MaxLength(100)] public List<ServiceEvidenceLine> Lines { get; init; } = [];
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ServiceEvidenceLine
{
    [Required, StringLength(64)] public string SourceLineCode { get; init; } = "";
    [EnumDataType(typeof(ServiceEvidenceOutcome))] public ServiceEvidenceOutcome Outcome { get; init; }
    [Required, StringLength(200)] public string Provider { get; init; } = "";
    [Required, StringLength(200)] public string EvidenceReference { get; init; } = "";
    [Required, StringLength(4000)] public string Description { get; init; } = "";
    public DateTimeOffset? PerformedAt { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AcceptRequestResults
{
    public Guid RequestId { get; init; }
    [Range(1, int.MaxValue)] public int Version { get; init; }
    public bool Accepted { get; init; }
    [StringLength(1000)] public string? Reason { get; init; }
}
public sealed record ServiceEvidenceDto(IReadOnlyList<ServiceEvidenceLine> Lines, string RecordedByAccountCode, DateTimeOffset RecordedAt);
public sealed record ResultAcceptanceDto(string Decision, string? Reason, string AccountCode, DateTimeOffset OccurredAt);
public sealed record PmmsEventResult(string DisplayCode, string Outcome, string? Reason, int SourceVersion, int Version);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ProcurementTransition
{
    [Range(1, int.MaxValue)] public int Version { get; init; }
    [EnumDataType(typeof(ProcurementRequestStatus))] public ProcurementRequestStatus Status { get; init; }
    [StringLength(1000)] public string? Reason { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ProcurementClose
{
    [Range(1, int.MaxValue)] public int Version { get; init; }
    [StringLength(1000)] public string? FailureReason { get; init; }
}
public interface IPmmsRequestSource
{
    string Source { get; }
    PmmsSimulatedEvent Normalize(PmmsSimulatedEvent input);
}
public interface IProcurementRequestRepository
{
    Task<ProcurementRequest?> GetAsync(string code, bool tracked, CancellationToken ct);
    Task<ProcurementRequest?> GetBySourceAsync(string source, string externalCode, CancellationToken ct);
    Task<PagedResult<ProcurementSummary>> ListAsync(ProcurementQuery query, CancellationToken ct, string? departmentCode = null, Guid? assigneeId = null);
    Task<PagedResult<ProcurementEventDto>> HistoryAsync(Guid id, int page, int pageSize, CancellationToken ct);
}
public interface IProcurementRequestService
{
    Task<PmmsEventResult> SimulateAsync(PmmsSimulatedEvent input, CancellationToken ct);
    Task<PagedResult<ProcurementSummary>> ListAsync(ProcurementQuery query, CancellationToken ct);
    Task<ProcurementDetail> GetAsync(string code, CancellationToken ct);
    Task<PagedResult<ProcurementEventDto>> HistoryAsync(string code, int page, int pageSize, CancellationToken ct);
    Task TransitionAsync(string code, ProcurementTransition request, CancellationToken ct);
    Task ReviewAsync(string code, ProcurementTransition request, CancellationToken ct);
    Task CompleteAsync(string code, ProcurementClose request, CancellationToken ct);
    Task RecordServicesAsync(string code, RecordServiceEvidence request, CancellationToken ct);
    Task AcceptResultsAsync(string code, AcceptRequestResults request, CancellationToken ct);
    Task AssignAsync(string code, AssignSourceRequest request, CancellationToken ct);
    Task<IReadOnlyList<RequestAssigneeDto>> AssigneesAsync(CancellationToken ct);
    Task<IReadOnlyList<RequestWorkloadDto>> WorkloadAsync(CancellationToken ct);
    Task<CSVC.UseCases.ServiceOrders.DTOs.ServiceOrderDto> StartRepairAsync(string code, StartSourceRepair request, CancellationToken ct);
}
public interface IProcurementWorkflow
{
    // Called inside the same inventory transaction as the document write.
    Task AttachAsync(string? sourceCode, RequestDocumentKind kind, string documentCode, Guid departmentId,
        IReadOnlyList<RequestDeviceBinding> bindings, Guid actorId, CancellationToken ct);
    Task PreventLinkedEditAsync(RequestDocumentKind kind, string documentCode, CancellationToken ct);
    Task<IReadOnlyList<RequestDocumentDto>> DocumentsAsync(ProcurementRequest request, CancellationToken ct);
    Task RequireCombinedDeliveryAsync(string allocationCode, CancellationToken ct);
}
