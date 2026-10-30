using CSVC.Core.DeviceReceipts;
using CSVC.Core.Common;
using CSVC.Core.Devices;

namespace CSVC.UseCases.DeviceReceipts.Models;

public sealed class DeviceReceiptReadModel
{
    public Guid Id { get; init; }
    public required string ReceiptCode { get; init; }
    public required string SupplierCode { get; init; }
    public required string SupplierName { get; init; }
    public string? CarrierName { get; init; }
    public string? RequestCode { get; init; }
    public DateOnly DeliveryDate { get; init; }
    public required string DeliveryBatch { get; init; }
    public required string DepartmentCode { get; init; }
    public required string DepartmentName { get; init; }
    public string? PaidInvoiceCode { get; init; }
    public DeviceReceiptStatus Status { get; init; }
    public required string CreatorAccountCode { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string? DecisionAccountCode { get; init; }
    public DateTimeOffset? DecidedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public string? DecisionReason { get; init; }
    public string? Notes { get; init; }
    public int ItemCount { get; init; }
    public int Version { get; init; }
}

public sealed class DeviceReceiptItemReadModel
{
    public Guid Id { get; init; }
    public required string CategoryCode { get; init; }
    public required string CategoryName { get; init; }
    public required string Name { get; init; }
    public required string DeviceCode { get; init; }
    public required string Barcode { get; init; }
    public DeviceCodeSource DeviceCodeSource { get; init; }
    public string? SerialNumber { get; init; }
    public string? ParentDeviceCode { get; init; }
    public bool Completed { get; init; }
}

public sealed class DeviceReceiptHistoryReadModel
{
    public required string ActionCode { get; init; }
    public AuditOutcome Outcome { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public string? ActorAccountCode { get; init; }
    public string? ActorFullName { get; init; }
    public string? ReasonCode { get; init; }
    public string? ChangedFields { get; init; }
    public string? BeforeData { get; init; }
    public string? AfterData { get; init; }
}
