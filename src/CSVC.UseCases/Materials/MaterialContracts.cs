using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using CSVC.Core.Common;
using CSVC.Core.Materials;
using CSVC.Core.Devices;
using CSVC.UseCases.Common;

namespace CSVC.UseCases.Materials;

public sealed class MaterialQuery
{
    [StringLength(200)] public string? Search { get; init; }
    [StringLength(32)] public string? MaterialCode { get; init; }
    [StringLength(32)] public string? WarehouseCode { get; init; }
    [StringLength(32)] public string? LocationCode { get; init; }
    [StringLength(32)] public string? DocumentCode { get; init; }
    [StringLength(32)] public string? RequestCode { get; init; }
    [StringLength(32)] public string? ServiceOrderCode { get; init; }
    [EnumDataType(typeof(EntityStatus))] public EntityStatus? Status { get; init; }
    [EnumDataType(typeof(MaterialKind))] public MaterialKind? MaterialKind { get; init; }
    [EnumDataType(typeof(MaterialDocumentKind))] public MaterialDocumentKind? Kind { get; init; }
    [EnumDataType(typeof(MaterialDocumentStatus))] public MaterialDocumentStatus? DocumentStatus { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    [Range(1, 1000000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class UnitWrite
{
    [StringLength(32)] public string? Code { get; init; }
    [Required, StringLength(200)] public string Name { get; init; } = "";
    [Range(0, 3)] public int DecimalPlaces { get; init; }
    [EnumDataType(typeof(EntityStatus))] public EntityStatus Status { get; init; }
    [Range(1, int.MaxValue)] public int? Version { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class MaterialWrite
{
    [StringLength(32)] public string? Code { get; init; }
    [Required, StringLength(200)] public string Name { get; init; } = "";
    [Required, StringLength(32)] public string UnitCode { get; init; } = "";
    [EnumDataType(typeof(MaterialKind))] public MaterialKind Kind { get; init; }
    [EnumDataType(typeof(EntityStatus))] public EntityStatus Status { get; init; }
    [StringLength(4000)] public string? Notes { get; init; }
    [Range(1, int.MaxValue)] public int? Version { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class WarehouseWrite
{
    [StringLength(32)] public string? Code { get; init; }
    [Required, StringLength(200)] public string Name { get; init; } = "";
    [EnumDataType(typeof(EntityStatus))] public EntityStatus Status { get; init; }
    [StringLength(4000)] public string? Notes { get; init; }
    [Range(1, int.MaxValue)] public int? Version { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class LocationWrite
{
    [StringLength(32)] public string? Code { get; init; }
    [Required, StringLength(200)] public string Name { get; init; } = "";
    [Required, StringLength(32)] public string WarehouseCode { get; init; } = "";
    [StringLength(32)] public string? AreaCode { get; init; }
    [EnumDataType(typeof(EntityStatus))] public EntityStatus Status { get; init; }
    [Range(1, int.MaxValue)] public int? Version { get; init; }
}
public sealed record UnitDto(string Code, string Name, int DecimalPlaces, EntityStatus Status, int Version);
public sealed record MaterialDto(string Code, string Name, MaterialKind Kind, string UnitCode, string UnitName, int DecimalPlaces, EntityStatus Status, string? Notes, int Version);
public sealed record WarehouseDto(string Code, string Name, string DepartmentCode, EntityStatus Status, string? Notes, int Version);
public sealed record LocationDto(string Code, string Name, string WarehouseCode, string WarehouseName, string? AreaCode, string? AreaName, EntityStatus Status, int Version);
public sealed record BalanceDto(string MaterialCode, string MaterialName, MaterialKind Kind, string UnitCode, string UnitName,
    string WarehouseCode, string WarehouseName, string LocationCode, string LocationName, decimal Quantity);
public sealed record MaterialReportRow(string MaterialCode, string MaterialName, string UnitCode, string UnitName,
    string WarehouseCode, string WarehouseName, string LocationCode, string LocationName,
    decimal Opening, decimal Receipts, decimal Issues, decimal Returns, decimal TransferIn, decimal TransferOut,
    decimal AdjustmentIn, decimal AdjustmentOut, decimal Closing);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class CreateMaterialDocument
{
    public Guid RequestId { get; init; }
    [EnumDataType(typeof(MaterialDocumentKind))] public MaterialDocumentKind Kind { get; init; }
    [Required, StringLength(1000)] public string Reason { get; init; } = "";
    [StringLength(4000)] public string? Notes { get; init; }
    public DateOnly? ReferenceDate { get; init; }
    [StringLength(200)] public string? Reference { get; init; }
    [StringLength(64)] public string? SupplierCode { get; init; }
    [StringLength(32)] public string? ServiceOrderCode { get; init; }
    [StringLength(32)] public string? RequestCode { get; init; }
    [StringLength(32)] public string? TargetDepartmentCode { get; init; }
    [StringLength(32)] public string? TargetResponsibleAccountCode { get; init; }
    [Required, MinLength(1), MaxLength(200)] public List<MaterialLineInput> Lines { get; init; } = [];
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdateMaterialDocument : CreateMaterialDocument
{
    [Range(1, int.MaxValue)] public int Version { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class MaterialLineInput
{
    [Required, StringLength(32)] public string MaterialCode { get; init; } = "";
    [StringLength(32)] public string? LocationCode { get; init; }
    [StringLength(32)] public string? ToLocationCode { get; init; }
    [Range(typeof(decimal), "-1000000000", "1000000000")] public decimal Quantity { get; init; }
    [EnumDataType(typeof(MaterialCondition))] public MaterialCondition Condition { get; init; }
    [StringLength(1000)] public string? ConditionNote { get; init; }
    [StringLength(32)] public string? DeviceCode { get; init; }
    public Guid? OriginalIssueLineId { get; init; }
    public Guid? RemovedMaterialId { get; init; }
    [StringLength(64)] public string? SourceLineCode { get; init; }
    [StringLength(100)] public string? LotOrSerial { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class MaterialAction
{
    public Guid RequestId { get; init; }
    [Range(1, int.MaxValue)] public int Version { get; init; }
    [StringLength(1000)] public string? Reason { get; init; }
    [Required, MaxLength(200)] public List<MaterialDeliveryDevice> Devices { get; init; } = [];
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class MaterialDeliveryDevice
{
    [Required, StringLength(32)] public string DeviceCode { get; init; } = "";
    [StringLength(64)] public string? SourceLineCode { get; init; }
    [EnumDataType(typeof(DeviceCondition))] public DeviceCondition Condition { get; init; }
    [StringLength(1000)] public string? Note { get; init; }
}
public sealed record MaterialDocumentSummary(string Code, MaterialDocumentKind Kind, MaterialDocumentStatus Status, string Reason,
    string CreatedByAccountCode, DateTimeOffset CreatedAt, string? PostedByAccountCode, DateTimeOffset? PostedAt,
    string? RequestCode, string? TargetDepartmentCode, string? TargetDepartmentName, int LineCount, int Version);
public sealed record MaterialLineDto(Guid Id, int LineNo, string MaterialCode, string MaterialName, MaterialKind MaterialKind,
    string UnitCode, string UnitName, int DecimalPlaces, string LocationCode, string LocationName, string WarehouseCode,
    string? ToLocationCode, decimal Quantity, MaterialCondition Condition, string? ConditionNote,
    string? DeviceCode, Guid? OriginalIssueLineId, Guid? RemovedMaterialId, string? SourceLineCode, string? LotOrSerial,
    decimal UsedQuantity, decimal ReturnedQuantity, decimal RecoverableQuantity);
public sealed record MaterialDocumentDetail(MaterialDocumentSummary Summary, DateOnly? ReferenceDate, string? Reference, string? Notes,
    string? SupplierCode, string? SupplierName, string? ApprovedByAccountCode, DateTimeOffset? ApprovedAt,
    string? ReceivedByAccountCode, DateTimeOffset? ReceivedAt, string? TargetResponsibleAccountCode,
    string? ServiceOrderCode, string? CombinedTransferCode, bool CombinedInternalUse,
    IReadOnlyList<MaterialDeliveryRequirement> EquipmentRequirements, IReadOnlyList<MaterialLineDto> Lines);
public sealed record MaterialDeliveryRequirement(string SourceLineCode, string Description, int Quantity, string? CategoryCode);
public sealed record MaterialHistoryDto(string Action, string? Reason, string ActorAccountCode, DateTimeOffset OccurredAt, string? BeforeData, string AfterData);
public sealed record MaterialMovementDto(string DocumentCode, MaterialDocumentKind Kind, string MaterialCode, string MaterialName,
    string UnitName, string LocationCode, string LocationName, decimal Delta, decimal BalanceAfter, string ActorAccountCode, DateTimeOffset OccurredAt);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RecordEquipmentChange
{
    public Guid RequestId { get; init; }
    [StringLength(32)] public string? ServiceOrderCode { get; init; }
    [Required, StringLength(32)] public string DeviceCode { get; init; } = "";
    [StringLength(32)] public string? ReplacesCode { get; init; }
    [EnumDataType(typeof(EquipmentChangeKind))] public EquipmentChangeKind Kind { get; init; }
    [Required, StringLength(200)] public string Target { get; init; } = "";
    [StringLength(4000)] public string? BeforeValue { get; init; }
    [StringLength(4000)] public string? AfterValue { get; init; }
    [Required, StringLength(1000)] public string Reason { get; init; } = "";
    [StringLength(4000)] public string? Notes { get; init; }
    public DateTimeOffset? PerformedAt { get; init; }
    [StringLength(200)] public string? Executor { get; init; }
    [Required, MaxLength(100)] public List<EquipmentMaterialUseInput> Uses { get; init; } = [];
    [Required, MaxLength(100)] public List<EquipmentRemovedInput> Removed { get; init; } = [];
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class EquipmentMaterialUseInput
{
    [EnumDataType(typeof(MaterialUseSource))] public MaterialUseSource Source { get; init; }
    public Guid? IssueLineId { get; init; }
    [StringLength(32)] public string? MaterialCode { get; init; }
    [StringLength(1000)] public string? Description { get; init; }
    [StringLength(200)] public string? Unit { get; init; }
    [Range(typeof(decimal), "0.001", "1000000000")] public decimal Quantity { get; init; }
    [StringLength(100)] public string? LotOrSerial { get; init; }
    [StringLength(1000)] public string? SourceReference { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class EquipmentRemovedInput
{
    [StringLength(32)] public string? MaterialCode { get; init; }
    [Required, StringLength(1000)] public string Description { get; init; } = "";
    [Required, StringLength(200)] public string Unit { get; init; } = "";
    [Range(typeof(decimal), "0.001", "1000000000")] public decimal Quantity { get; init; }
    public bool Reusable { get; init; }
    [Required, StringLength(1000)] public string ConditionNote { get; init; } = "";
    [EnumDataType(typeof(RemovedDisposition))] public RemovedDisposition Disposition { get; init; }
    [StringLength(1000)] public string? RecipientOrEvidence { get; init; }
    [StringLength(100)] public string? LotOrSerial { get; init; }
}
public sealed record EquipmentUseDto(Guid Id, MaterialUseSource Source, Guid? IssueLineId, string? MaterialCode, string Description, string Unit, decimal Quantity, string? LotOrSerial, string? SourceReference);
public sealed record EquipmentRemovedDto(Guid Id, string? MaterialCode, string Description, string Unit, decimal Quantity,
    bool Reusable, string ConditionNote, RemovedDisposition Disposition, string? RecipientOrEvidence, string? LotOrSerial, string? RecoveryDocumentCode);
public sealed record EquipmentChangeDto(string Code, string DeviceCode, string DeviceName, string? ServiceOrderCode,
    string? ReplacesCode, string? ReplacedByCode, EquipmentChangeKind Kind, string Target, string? BeforeValue, string? AfterValue,
    string Reason, string? Notes, string RecordedByAccountCode, string RecordedByName, string Executor, DateTimeOffset PerformedAt,
    DateTimeOffset RecordedAt, IReadOnlyList<EquipmentUseDto> Uses, IReadOnlyList<EquipmentRemovedDto> Removed);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RecordMaterialWorkUse
{
    public Guid RequestId { get; init; }
    [StringLength(32)] public string? ServiceOrderCode { get; init; }
    public Guid IssueLineId { get; init; }
    [Range(typeof(decimal), "0.001", "1000000000")] public decimal Quantity { get; init; }
    [Required, StringLength(1000)] public string Reason { get; init; } = "";
    [StringLength(32)] public string? ReplacesCode { get; init; }
}
public sealed record MaterialWorkUseDto(string Code, Guid IssueLineId, string MaterialCode, string MaterialName,
    string UnitName, decimal Quantity, string Reason, string ActorAccountCode, DateTimeOffset OccurredAt, string? ReplacesCode, string? ReplacedByCode);

public interface IMaterialService
{
    Task<PagedResult<UnitDto>> UnitsAsync(MaterialQuery query, CancellationToken ct);
    Task<UnitDto> SaveUnitAsync(string? code, UnitWrite input, CancellationToken ct);
    Task<PagedResult<MaterialDto>> MaterialsAsync(MaterialQuery query, CancellationToken ct);
    Task<MaterialDto> SaveMaterialAsync(string? code, MaterialWrite input, CancellationToken ct);
    Task<PagedResult<WarehouseDto>> WarehousesAsync(MaterialQuery query, CancellationToken ct);
    Task<WarehouseDto> SaveWarehouseAsync(string? code, WarehouseWrite input, CancellationToken ct);
    Task<PagedResult<LocationDto>> LocationsAsync(MaterialQuery query, CancellationToken ct);
    Task<LocationDto> SaveLocationAsync(string? code, LocationWrite input, CancellationToken ct);
    Task<PagedResult<BalanceDto>> BalancesAsync(MaterialQuery query, CancellationToken ct);
    Task<PagedResult<MaterialReportRow>> ReportAsync(MaterialQuery query, CancellationToken ct);
    Task<PagedResult<MaterialMovementDto>> MovementsAsync(MaterialQuery query, CancellationToken ct);
    Task<PagedResult<MaterialDocumentSummary>> DocumentsAsync(MaterialQuery query, CancellationToken ct);
    Task<MaterialDocumentDetail> GetDocumentAsync(string code, CancellationToken ct);
    Task<MaterialDocumentDetail> CreateDocumentAsync(CreateMaterialDocument input, CancellationToken ct);
    Task UpdateDocumentAsync(string code, UpdateMaterialDocument input, CancellationToken ct);
    Task ActAsync(string code, string action, MaterialAction input, CancellationToken ct);
    Task<PagedResult<MaterialHistoryDto>> HistoryAsync(string code, int page, int pageSize, CancellationToken ct);
    Task<EquipmentChangeDto> RecordChangeAsync(RecordEquipmentChange input, CancellationToken ct);
    Task<PagedResult<EquipmentChangeDto>> ChangesAsync(string? deviceCode, MaterialQuery query, CancellationToken ct);
    Task<MaterialWorkUseDto> RecordWorkUseAsync(RecordMaterialWorkUse input, CancellationToken ct);
    Task<PagedResult<MaterialWorkUseDto>> WorkUsesAsync(MaterialQuery query, CancellationToken ct);
}
