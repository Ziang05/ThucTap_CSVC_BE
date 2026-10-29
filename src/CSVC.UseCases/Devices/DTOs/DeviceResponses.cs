using CSVC.Core.Common;
using CSVC.Core.Devices;
using CSVC.UseCases.Common;

namespace CSVC.UseCases.Devices.DTOs;

public sealed record DeviceDto(
    string DeviceCode, string? Barcode, string Name, string CategoryCode, string CategoryName,
    string DepartmentCode, string DepartmentName, string? AreaCode, EntityStatus Status,
    DeviceCondition Condition, DeviceAvailability Availability, string? ResponsibleAccountCode,
    string? ResponsibleFullName, string? ManufacturerCode, string? ManufacturerName,
    string? Model, string? SerialNumber, DateOnly? PurchaseDate, string? PaidInvoiceCode,
    decimal? PurchasePrice, DateOnly? WarrantyExpiryDate, string? Notes, string Version);

public sealed record DeviceHistoryDto(
    string ActionCode, string Outcome, DateTimeOffset OccurredAt, string? ActorAccountCode,
    string? ActorFullName, string? ReasonCode, string? ChangedFields, string? BeforeData, string? AfterData,
    string EventType = "DEVICE", string? BusinessCode = null, string? DeviceCode = null, string? DeviceName = null,
    string? DepartmentCode = null);

public sealed record DeviceHistoryPage(PagedResult<DeviceHistoryDto> Result);
