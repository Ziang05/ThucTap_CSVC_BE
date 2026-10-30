using CSVC.Core.DeviceReceipts;
using CSVC.UseCases.Common;

namespace CSVC.UseCases.DeviceReceipts.DTOs;

public sealed record DeviceReceiptItemDto(string CategoryCode, string CategoryName, string Name, string DeviceCode,
    string Barcode, string DeviceCodeSource, string? SerialNumber, bool Completed, string? ParentDeviceCode = null);

public sealed record DeviceReceiptDto(string ReceiptCode, string SupplierCode, string SupplierName, string? CarrierName,
    string? RequestCode, DateOnly DeliveryDate, string DeliveryBatch, string RequestingDepartmentCode,
    string RequestingDepartmentName, string? PaidInvoiceCode, DeviceReceiptStatus Status, string CreatorAccountCode,
    DateTimeOffset CreatedAt, string? DecisionAccountCode, DateTimeOffset? DecidedAt, DateTimeOffset? CompletedAt,
    string? DecisionReason, string? Notes, int ItemCount, int Version, IReadOnlyList<DeviceReceiptItemDto>? Items = null);

public sealed record DeviceReceiptHistoryDto(string ActionCode, string Outcome, DateTimeOffset OccurredAt,
    string? ActorAccountCode, string? ActorFullName, string? ReasonCode, string? ChangedFields, string? BeforeData, string? AfterData);

public sealed record DeviceReceiptListResponse(PagedResult<DeviceReceiptDto> Result);
