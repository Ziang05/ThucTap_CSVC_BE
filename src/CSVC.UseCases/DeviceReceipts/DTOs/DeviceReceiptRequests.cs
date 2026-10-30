using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using CSVC.Core.DeviceReceipts;

namespace CSVC.UseCases.DeviceReceipts.DTOs;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class CreateDeviceReceiptRequest
{
    [Required, StringLength(32)] public string SupplierCode { get; init; } = "";
    [StringLength(200)] public string? CarrierName { get; init; }
    [StringLength(64)] public string? RequestCode { get; init; }
    public DateOnly DeliveryDate { get; init; }
    [Required, StringLength(100)] public string DeliveryBatch { get; init; } = "";
    [Required, StringLength(32)] public string RequestingDepartmentCode { get; init; } = "";
    [StringLength(64)] public string? PaidInvoiceCode { get; init; }
    [Required, MinLength(1)] public List<DeviceReceiptItemRequest> Items { get; init; } = [];
    [StringLength(4000)] public string? Notes { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdateDeviceReceiptRequest : CreateDeviceReceiptRequest { }

public sealed class DeviceReceiptItemRequest
{
    [Required, StringLength(32)] public string CategoryCode { get; init; } = "";
    [Required, StringLength(200)] public string Name { get; init; } = "";
    [StringLength(32)] public string? DeviceCode { get; init; }
    [StringLength(100)] public string? SerialNumber { get; init; }
    [StringLength(32)] public string? ParentDeviceCode { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ReceiptDecisionRequest
{
    [StringLength(1000)] public string? Reason { get; init; }
}

public sealed class DeviceReceiptListQuery
{
    [StringLength(32)] public string? ReceiptCode { get; init; }
    [StringLength(32)] public string? SupplierCode { get; init; }
    [StringLength(32)] public string? RequestingDepartmentCode { get; init; }
    [StringLength(32)] public string? CreatedByAccountCode { get; init; }
    [EnumDataType(typeof(DeviceReceiptStatus))] public DeviceReceiptStatus? Status { get; init; }
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
    [Range(1, 1_000_000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
}

public sealed class DeviceReceiptHistoryQuery
{
    [Range(1, 1_000_000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
}
