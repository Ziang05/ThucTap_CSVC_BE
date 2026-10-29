using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using CSVC.Core.Devices;

namespace CSVC.UseCases.Devices.DTOs;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateDeviceRequest
{
    [StringLength(32)] public string? DeviceCode { get; init; }
    [Required, StringLength(200)] public string Name { get; init; } = "";
    [Required, StringLength(32)] public string CategoryCode { get; init; } = "";
    [Required, StringLength(32)] public string DepartmentCode { get; init; } = "";
    [StringLength(32)] public string? ResponsibleAccountCode { get; init; }
    [EnumDataType(typeof(DeviceCondition))] public DeviceCondition Condition { get; init; } = DeviceCondition.Good;
    [StringLength(100)] public string? SerialNumber { get; init; }
    [StringLength(32)] public string? ManufacturerCode { get; init; }
    [StringLength(120)] public string? Model { get; init; }
    public DateOnly? PurchaseDate { get; init; }
    [StringLength(64)] public string? PaidInvoiceCode { get; init; }
    [Range(typeof(decimal), "0", "9999999999999999.99")] public decimal? PurchasePrice { get; init; }
    public DateOnly? WarrantyExpiryDate { get; init; }
    [StringLength(32)] public string? AreaCode { get; init; }
    [StringLength(4000)] public string? Notes { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdateDeviceRequest
{
    [Required, StringLength(200)] public string Name { get; init; } = "";
    [StringLength(100)] public string? SerialNumber { get; init; }
    [StringLength(32)] public string? ManufacturerCode { get; init; }
    [StringLength(120)] public string? Model { get; init; }
    public DateOnly? PurchaseDate { get; init; }
    [StringLength(64)] public string? PaidInvoiceCode { get; init; }
    [Range(typeof(decimal), "0", "9999999999999999.99")] public decimal? PurchasePrice { get; init; }
    public DateOnly? WarrantyExpiryDate { get; init; }
    [StringLength(4000)] public string? Notes { get; init; }
    [RegularExpression("^[A-Fa-f0-9]{64}$")] public string? Version { get; init; }
}

public enum DeviceStatusFilter { Active, Locked }
public enum DeviceAvailability { Available, Reserved, OnLoan, TransferPending }
public enum DeviceSortBy { DeviceCode, Name, Category, Department, Status }
public enum DeviceSortDirection { Asc, Desc }

public sealed class DeviceListQuery
{
    [StringLength(200)] public string? Search { get; init; }
    [StringLength(32)] public string? CategoryCode { get; init; }
    [EnumDataType(typeof(DeviceStatusFilter))] public DeviceStatusFilter? Status { get; init; }
    [EnumDataType(typeof(DeviceCondition))] public DeviceCondition? Condition { get; init; }
    [StringLength(32)] public string? DepartmentCode { get; init; }
    [StringLength(32)] public string? AreaCode { get; init; }
    [EnumDataType(typeof(DeviceAvailability))] public DeviceAvailability? Availability { get; init; }
    [EnumDataType(typeof(DeviceSortBy))]
    public DeviceSortBy SortBy { get; init; } = DeviceSortBy.DeviceCode;
    [EnumDataType(typeof(DeviceSortDirection))]
    public DeviceSortDirection SortDirection { get; init; } = DeviceSortDirection.Asc;
    [Range(1, 1_000_000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
}

public sealed class DeviceHistoryQuery
{
    [RegularExpression("(?i)^(DEVICE|RECEIPT|LOAN|TRANSFER)$")] public string? EventType { get; init; }
    [StringLength(32)] public string? DeviceCode { get; init; }
    [StringLength(32)] public string? DepartmentCode { get; init; }
    [StringLength(32)] public string? ActorAccountCode { get; init; }
    public DateTimeOffset? FromDate { get; init; }
    public DateTimeOffset? ToDate { get; init; }
    [Range(1, 1_000_000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
}
