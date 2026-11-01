using CSVC.Core.ServiceOrders;

namespace CSVC.UseCases.ServiceOrders.DTOs;

public sealed record ServiceOrderDto(string DisplayCode, ServiceOrderType OrderType, ServiceWorkflowStatus Status,
    string CreatedByAccountCode, DateTimeOffset CreatedAt, string? ApprovedByAccountCode, DateTimeOffset? ApprovedAt,
    string? CanceledByAccountCode, DateTimeOffset? CanceledAt, string? CancelReason, DateTimeOffset? CompletedAt,
    string? Notes, int Version, int ItemCount, IReadOnlyList<ServiceOrderItemDto> Items, IReadOnlyList<ExportSlipDto> ExportSlips);

public sealed record ServiceOrderItemDto(int ItemNo, string DeviceCode, string DeviceName, string? Serial,
    ExecutionMethod ExecutionMethod, ServiceItemStatus? ExecutionStatus, string? WorkDescription,
    string? ResultDescription, string? ResultRecordedByAccountCode, DateTimeOffset? ResultRecordedAt, string? Notes, int Version);

public sealed record ExportSlipDto(string DisplayCode, string ServiceOrderCode, ServiceOrderType OrderType,
    int SequenceNo, ServiceWorkflowStatus Status, string FacilityName, string? FacilityAddress, string? FacilityContact,
    DateTimeOffset? PlannedExportAt, string? Notes, string CreatedByAccountCode, DateTimeOffset CreatedAt,
    ExportResult? Result, string? ResultRecordedByAccountCode, DateTimeOffset? ResultRecordedAt,
    string? RecipientName, string? RecipientContact, string? FailureReason, string? EvidenceRef,
    DateTimeOffset? CanceledAt, DateTimeOffset? CompletedAt, int Version, IReadOnlyList<ExportSlipItemDto> Items);

public sealed record ExportSlipItemDto(int ItemNo, string DeviceCode, string DeviceName, string? Serial);

public sealed record ServiceHistoryDto(string ActionCode, string Outcome, DateTimeOffset OccurredAt,
    string? ActorAccountCode, int? ItemNo, string? RelatedExportSlipCode, string? ReasonCode, string? BeforeData, string? AfterData);
