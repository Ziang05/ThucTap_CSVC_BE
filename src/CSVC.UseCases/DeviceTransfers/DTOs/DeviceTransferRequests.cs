using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using CSVC.Core.DeviceTransfers;
using CSVC.Core.Devices;

namespace CSVC.UseCases.DeviceTransfers.DTOs;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class CreateDeviceTransferRequest
{
    [Required, StringLength(32)] public string SourceDepartmentCode { get; init; } = "";
    [Required, StringLength(32)] public string TargetDepartmentCode { get; init; } = "";
    [Required, StringLength(32)] public string TargetResponsibleAccountCode { get; init; } = "";
    [Required, StringLength(1000)] public string Reason { get; init; } = "";
    [Required, MinLength(1)] public List<DeviceTransferDeviceRequest> Items { get; init; } = [];
    [StringLength(4000)] public string? Notes { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdateDeviceTransferRequest : CreateDeviceTransferRequest { }

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class DeviceTransferDeviceRequest
{
    [Required, StringLength(32)] public string DeviceCode { get; init; } = "";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class TransferDecisionRequest
{
    [StringLength(1000)] public string? Reason { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class TransferHandoverRequest
{
    [Required, StringLength(32)] public string SourceHandoverAccountCode { get; init; } = "";
    [Required, StringLength(32)] public string TargetHandoverAccountCode { get; init; } = "";
    [Required, MinLength(1)] public List<TransferHandoverItemRequest> Items { get; init; } = [];
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class TransferHandoverItemRequest
{
    [Required, StringLength(32)] public string DeviceCode { get; init; } = "";
    [EnumDataType(typeof(DeviceCondition))] public DeviceCondition Condition { get; init; } = DeviceCondition.Good;
    [StringLength(1000)] public string? Note { get; init; }
}

public sealed class DeviceTransferListQuery
{
    [StringLength(32)] public string? TransferCode { get; init; }
    [StringLength(32)] public string? DepartmentCode { get; init; }
    [EnumDataType(typeof(DeviceTransferStatus))] public DeviceTransferStatus? Status { get; init; }
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
    [Range(1, 1_000_000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
}

public sealed class DeviceTransferHistoryQuery
{
    [Range(1, 1_000_000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
}
