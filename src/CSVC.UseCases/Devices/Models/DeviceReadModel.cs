using CSVC.Core.Common;
using CSVC.Core.Devices;

namespace CSVC.UseCases.Devices.Models;

public sealed class DeviceReadModel
{
    public Guid Id { get; init; }
    public required string DeviceCode { get; init; }
    public required string Barcode { get; init; }
    public required string Name { get; init; }
    public required string CategoryCode { get; init; }
    public required string CategoryName { get; init; }
    public required string DepartmentCode { get; init; }
    public required string DepartmentName { get; init; }
    public string? AreaCode { get; init; }
    public EntityStatus Status { get; init; }
    public DeviceCondition Condition { get; init; }
    public string? ResponsibleAccountCode { get; init; }
    public string? ResponsibleFullName { get; init; }
    public string? ManufacturerCode { get; init; }
    public string? ManufacturerName { get; init; }
    public string? Model { get; init; }
    public string? SerialNumber { get; init; }
    public DateOnly? PurchaseDate { get; init; }
    public string? PaidInvoiceCode { get; init; }
    public decimal? PurchasePrice { get; init; }
    public DateOnly? WarrantyExpiryDate { get; init; }
    public string? Notes { get; init; }
    public int Version { get; init; }
    public bool HasHold { get; init; }
    public DeviceOperationKind? HoldKind { get; init; }
    public bool IsOnLoan { get; init; }
}

public sealed class DeviceHistoryReadModel
{
    public required string EventType { get; init; }
    public required string ActionCode { get; init; }
    public AuditOutcome Outcome { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public string? BusinessCode { get; init; }
    public string? DeviceCode { get; init; }
    public string? DeviceName { get; init; }
    public string? DepartmentCode { get; init; }
    public string? ActorAccountCode { get; init; }
    public string? ActorFullName { get; init; }
    public string? ReasonCode { get; init; }
    public string? ChangedFields { get; init; }
    public string? BeforeData { get; init; }
    public string? AfterData { get; init; }
}
