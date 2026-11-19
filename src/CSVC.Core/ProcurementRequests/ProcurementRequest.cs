namespace CSVC.Core.ProcurementRequests;

public enum ProcurementRequestType { Purchase, Repair }
public enum ProcurementRequestStatus { Received, InProgress, WaitingForGoods, WaitingForExternal, AwaitingHandover, Completed, Cancelled }
public enum ProcurementLineKind { Equipment, Material, Service }
public enum EquipmentSpecialization { Unclassified, Common, Specialized }
public enum RequestDocumentKind { Receipt, Allocation, Repair, InternalUse, MaterialReceipt, MaterialIssue, MaterialReturn, ServiceEvidence }

public sealed class ProcurementRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string DisplayCode { get; set; }
    public string Source { get; set; } = "PMMS_SIMULATED";
    public required string ExternalCode { get; set; }
    public ProcurementRequestType RequestType { get; set; }
    public required string Title { get; set; }
    public required string RequestingDepartmentCode { get; set; }
    public string? Contact { get; set; }
    public string? ContractReference { get; set; }
    public string? SupplierReference { get; set; }
    public string? Notes { get; set; }
    public required string SourceStatus { get; set; }
    public DateTimeOffset SourceCreatedAt { get; set; }
    public DateTimeOffset SourceUpdatedAt { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public int SourceVersion { get; set; }
    public int Version { get; set; } = 1;
    public int WorkflowVersion { get; set; } = 1;
    public string? ServiceEvidenceJson { get; set; }
    public Guid? ServiceRecordedByAccountId { get; set; }
    public DateTimeOffset? ServiceRecordedAt { get; set; }
    public string? AcceptanceDecision { get; set; }
    public string? AcceptanceReason { get; set; }
    public string? AcceptanceScopeHash { get; set; }
    public Guid? AcceptedByAccountId { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public ProcurementRequestStatus Status { get; set; }
    public string? MappingIssue { get; set; }
    public bool RequiresReview { get; set; }
    public string? PendingSourcePayload { get; set; }
    public string? Result { get; set; }
    public string? ResultReason { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public ICollection<ProcurementRequestLine> Lines { get; set; } = new List<ProcurementRequestLine>();
}

public sealed class ProcurementRequestLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequestId { get; set; }
    public required string SourceLineCode { get; set; }
    public ProcurementLineKind Kind { get; set; }
    public required string Description { get; set; }
    public decimal Quantity { get; set; }
    public required string Unit { get; set; }
    public string? CategoryCode { get; set; }
    public string? DeviceCode { get; set; }
    public string? DamageReportCode { get; set; }
    public EquipmentSpecialization Specialization { get; set; }
}

public sealed class ProcurementRequestEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequestId { get; set; }
    public Guid? ExternalEventId { get; set; }
    public Guid? CommandId { get; set; }
    public int? SourceVersion { get; set; }
    public required string Action { get; set; }
    public required string Outcome { get; set; }
    public string? Reason { get; set; }
    public string? PayloadHash { get; set; }
    public string? Payload { get; set; }
    public string? Response { get; set; }
    public Guid ActorAccountId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}

public sealed class ProcurementDocumentLink
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequestId { get; set; }
    public RequestDocumentKind Kind { get; set; }
    public required string DocumentCode { get; set; }
    public required string BindingsJson { get; set; }
    public Guid LinkedByAccountId { get; set; }
    public DateTimeOffset LinkedAt { get; set; }
}
