using CSVC.Core.Common;

namespace CSVC.Core.Materials;

public enum MaterialKind { Material, Tool }
public enum MaterialDocumentKind { Receipt, Issue, Adjustment, ReturnUnused, ReceiveRecovered, Transfer }
public enum MaterialDocumentStatus { Ready, PendingApproval, Approved, AwaitingReceipt, Completed, Cancelled, Rejected }
public enum MaterialCondition { New, Reusable }
public enum EquipmentChangeKind { Replace, Add, Upgrade, Remove, Repair, Inspect, WorkResult }
public enum RemovedDisposition { ReturnToStock, ReturnToDepartment, Dispose, KeepWithDevice }
public enum MaterialUseSource { Warehouse, Supplier, Department, Other }

public sealed class MaterialUnit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Code { get; set; }
    public required string Name { get; set; }
    public int DecimalPlaces { get; set; }
    public EntityStatus Status { get; set; }
    public int Version { get; set; } = 1;
}
public sealed class MaterialItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Code { get; set; }
    public required string Name { get; set; }
    public MaterialKind Kind { get; set; }
    public Guid UnitId { get; set; }
    public string? Notes { get; set; }
    public EntityStatus Status { get; set; }
    public int Version { get; set; } = 1;
}
public sealed class MaterialWarehouse
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Code { get; set; }
    public required string Name { get; set; }
    public Guid DepartmentId { get; set; }
    public string? Notes { get; set; }
    public EntityStatus Status { get; set; }
    public int Version { get; set; } = 1;
}
public sealed class MaterialLocation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Code { get; set; }
    public required string Name { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid? AreaId { get; set; }
    public EntityStatus Status { get; set; }
    public int Version { get; set; } = 1;
}
public sealed class MaterialBalance
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MaterialId { get; set; }
    public Guid LocationId { get; set; }
    public decimal Quantity { get; set; }
    public int Version { get; set; } = 1;
}
public sealed class MaterialDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Code { get; set; }
    public MaterialDocumentKind Kind { get; set; }
    public MaterialDocumentStatus Status { get; set; }
    public Guid DepartmentId { get; set; }
    public Guid CreateRequestId { get; set; }
    public required string CreateHash { get; set; }
    public Guid CreatedByAccountId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateOnly? ReferenceDate { get; set; }
    public string? Reference { get; set; }
    public required string Reason { get; set; }
    public string? Notes { get; set; }
    public Guid? SupplierId { get; set; }
    public Guid? ServiceOrderId { get; set; }
    public Guid? ProcurementRequestId { get; set; }
    public Guid? TargetDepartmentId { get; set; }
    public Guid? TargetResponsibleAccountId { get; set; }
    public Guid? ApprovedByAccountId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public Guid? PostedByAccountId { get; set; }
    public DateTimeOffset? PostedAt { get; set; }
    public Guid? ReceivedByAccountId { get; set; }
    public DateTimeOffset? ReceivedAt { get; set; }
    public int Version { get; set; } = 1;
    public ICollection<MaterialDocumentLine> Lines { get; set; } = new List<MaterialDocumentLine>();
}
public sealed class MaterialDocumentLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DocumentId { get; set; }
    public int LineNo { get; set; }
    public Guid MaterialId { get; set; }
    public Guid LocationId { get; set; }
    public Guid? ToLocationId { get; set; }
    public decimal Quantity { get; set; }
    public MaterialCondition Condition { get; set; }
    public string? ConditionNote { get; set; }
    public Guid? DeviceId { get; set; }
    public Guid? OriginalIssueLineId { get; set; }
    public Guid? RemovedMaterialId { get; set; }
    public string? SourceLineCode { get; set; }
    public string? LotOrSerial { get; set; }
    public required string MaterialCodeSnapshot { get; set; }
    public required string MaterialNameSnapshot { get; set; }
    public required string UnitCodeSnapshot { get; set; }
    public required string UnitNameSnapshot { get; set; }
}
public sealed class MaterialLedgerEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DocumentId { get; set; }
    public Guid LineId { get; set; }
    public int Leg { get; set; }
    public Guid MaterialId { get; set; }
    public Guid LocationId { get; set; }
    public decimal Delta { get; set; }
    public decimal BalanceAfter { get; set; }
    public Guid ActorAccountId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
public sealed class MaterialDocumentHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DocumentId { get; set; }
    public Guid? RequestId { get; set; }
    public string? Fingerprint { get; set; }
    public Guid ActorAccountId { get; set; }
    public required string Action { get; set; }
    public string? Reason { get; set; }
    public string? BeforeData { get; set; }
    public required string AfterData { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
public sealed class MaterialCatalogHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Catalog { get; set; }
    public Guid TargetId { get; set; }
    public required string Code { get; set; }
    public Guid ActorAccountId { get; set; }
    public required string Action { get; set; }
    public string? BeforeData { get; set; }
    public required string AfterData { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}

// Change records describe work on equipment. They do not create child Device records or alter stock twice.
public sealed class EquipmentChange
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Code { get; set; }
    public Guid DeviceId { get; set; }
    public Guid? ServiceOrderId { get; set; }
    public Guid? IssueDocumentId { get; set; }
    public Guid? ReplacesId { get; set; }
    public Guid RequestId { get; set; }
    public required string Fingerprint { get; set; }
    public EquipmentChangeKind Kind { get; set; }
    public required string Target { get; set; }
    public string? BeforeValue { get; set; }
    public string? AfterValue { get; set; }
    public required string Reason { get; set; }
    public string? Notes { get; set; }
    public Guid RecordedByAccountId { get; set; }
    public required string Executor { get; set; }
    public DateTimeOffset PerformedAt { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public ICollection<EquipmentMaterialUse> Uses { get; set; } = new List<EquipmentMaterialUse>();
    public ICollection<EquipmentRemovedMaterial> Removed { get; set; } = new List<EquipmentRemovedMaterial>();
}
public sealed class EquipmentMaterialUse
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ChangeId { get; set; }
    public MaterialUseSource Source { get; set; }
    public Guid? IssueLineId { get; set; }
    public Guid? MaterialId { get; set; }
    public required string Description { get; set; }
    public required string Unit { get; set; }
    public decimal Quantity { get; set; }
    public string? LotOrSerial { get; set; }
    public string? SourceReference { get; set; }
}
public sealed class EquipmentRemovedMaterial
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ChangeId { get; set; }
    public Guid? MaterialId { get; set; }
    public required string Description { get; set; }
    public required string Unit { get; set; }
    public decimal Quantity { get; set; }
    public bool Reusable { get; set; }
    public required string ConditionNote { get; set; }
    public RemovedDisposition Disposition { get; set; }
    public string? RecipientOrEvidence { get; set; }
    public string? LotOrSerial { get; set; }
}

// Consumption for work that is not tied to a particular device (for example preparation work).
public sealed class MaterialWorkUse
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Code { get; set; }
    public Guid IssueLineId { get; set; }
    public Guid? ReplacesId { get; set; }
    public Guid RequestId { get; set; }
    public required string Fingerprint { get; set; }
    public decimal Quantity { get; set; }
    public required string Reason { get; set; }
    public Guid ActorAccountId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
