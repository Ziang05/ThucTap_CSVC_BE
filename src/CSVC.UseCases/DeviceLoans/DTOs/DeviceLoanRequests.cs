using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using CSVC.Core.DeviceLoans;
using CSVC.Core.Devices;

namespace CSVC.UseCases.DeviceLoans.DTOs;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class CreateDeviceLoanRequest
{
    [Required, StringLength(1000)] public string Purpose { get; init; } = "";
    public DateTimeOffset DueAt { get; init; }
    [Required, MinLength(1)] public List<DeviceLoanDeviceRequest> Items { get; init; } = [];
    [StringLength(4000)] public string? Notes { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdateDeviceLoanRequest : CreateDeviceLoanRequest { }

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class DeviceLoanDeviceRequest
{
    [Required, StringLength(32)] public string DeviceCode { get; init; } = "";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class LoanDecisionRequest
{
    [StringLength(1000)] public string? Reason { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class LoanHandoverRequest
{
    [Required, MinLength(1)] public List<LoanHandoverItemRequest> Items { get; init; } = [];
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class LoanHandoverItemRequest
{
    [Required, StringLength(32)] public string DeviceCode { get; init; } = "";
    [EnumDataType(typeof(DeviceCondition))] public DeviceCondition Condition { get; init; } = DeviceCondition.Good;
    [StringLength(1000)] public string? Note { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class LoanReturnRequest
{
    [Required, MinLength(1)] public List<LoanReturnItemRequest> Items { get; init; } = [];
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class LoanReturnItemRequest
{
    [Required, StringLength(32)] public string DeviceCode { get; init; } = "";
    [Required, EnumDataType(typeof(DeviceCondition))] public DeviceCondition? Condition { get; init; }
    [StringLength(1000)] public string? Note { get; init; }
}

public sealed class DeviceLoanListQuery
{
    [StringLength(32)] public string? LoanCode { get; init; }
    [StringLength(32)] public string? BorrowerAccountCode { get; init; }
    [StringLength(32)] public string? DepartmentCode { get; init; }
    [EnumDataType(typeof(DeviceLoanStatus))] public DeviceLoanStatus? Status { get; init; }
    public bool? Overdue { get; init; }
    [Range(1, 1_000_000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
}

public sealed class DeviceLoanHistoryQuery
{
    [Range(1, 1_000_000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
}
