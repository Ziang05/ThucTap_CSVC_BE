using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using CSVC.Core.ServiceOrders;

namespace CSVC.UseCases.ServiceOrders.DTOs;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateServiceOrderRequest
{
    public Guid RequestId { get; init; }
    [EnumDataType(typeof(ServiceOrderType))] public ServiceOrderType OrderType { get; init; }
    [Required, MinLength(1), MaxLength(100)] public List<ServiceItemRequest> Items { get; init; } = [];
    [Required, MaxLength(100)] public List<CreateExportSlipRequest> ExportSlips { get; init; } = [];
    [StringLength(4000)] public string? Notes { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ServiceItemRequest
{
    [Required, StringLength(32)] public string DeviceCode { get; init; } = "";
    [EnumDataType(typeof(ExecutionMethod))] public ExecutionMethod ExecutionMethod { get; init; }
    [StringLength(4000)] public string? WorkDescription { get; init; }
    [StringLength(4000)] public string? Notes { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateExportSlipRequest
{
    [Required, StringLength(255)] public string FacilityName { get; init; } = "";
    [StringLength(4000)] public string? FacilityAddress { get; init; }
    [StringLength(1000)] public string? FacilityContact { get; init; }
    public DateTimeOffset? PlannedExportAt { get; init; }
    [Required, MinLength(1), MaxLength(100)] public List<string> DeviceCodes { get; init; } = [];
    [StringLength(4000)] public string? Notes { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ServiceTransitionRequest
{
    public Guid RequestId { get; init; }
    [Range(0, int.MaxValue)] public int? Version { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CancelServiceOrderRequest
{
    public Guid RequestId { get; init; }
    [Range(0, int.MaxValue)] public int? Version { get; init; }
    [Required, StringLength(1000)] public string Reason { get; init; } = "";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RecordServiceResultsRequest
{
    public Guid RequestId { get; init; }
    [Range(0, int.MaxValue)] public int? Version { get; init; }
    [Required, MinLength(1), MaxLength(100)] public List<ServiceItemResultRequest> Items { get; init; } = [];
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ServiceItemResultRequest
{
    [Range(1, 100)] public int ItemNo { get; init; }
    [EnumDataType(typeof(ServiceItemStatus))] public ServiceItemStatus Result { get; init; }
    [Required, StringLength(4000)] public string Description { get; init; } = "";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RecordExportResultRequest
{
    public Guid RequestId { get; init; }
    [Range(0, int.MaxValue)] public int? Version { get; init; }
    [EnumDataType(typeof(ExportResult))] public ExportResult Result { get; init; }
    [StringLength(255)] public string? RecipientName { get; init; }
    [StringLength(255)] public string? RecipientContact { get; init; }
    [StringLength(4000)] public string? FailureReason { get; init; }
    [StringLength(4000)] public string? EvidenceRef { get; init; }
}

public sealed class ServiceOrderListQuery
{
    [StringLength(64)] public string? Search { get; init; }
    [EnumDataType(typeof(ServiceOrderType))] public ServiceOrderType? OrderType { get; init; }
    [EnumDataType(typeof(ServiceWorkflowStatus))] public ServiceWorkflowStatus? Status { get; init; }
    [Range(1, 1_000_000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
}

public sealed class ExportSlipListQuery
{
    [StringLength(64)] public string? ServiceOrderCode { get; init; }
    [EnumDataType(typeof(ServiceWorkflowStatus))] public ServiceWorkflowStatus? Status { get; init; }
    [Range(1, 1_000_000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
}

public sealed class ServiceHistoryQuery
{
    [Range(1, 1_000_000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
}
