using System.ComponentModel.DataAnnotations;
using CSVC.Core.Devices;

namespace CSVC.UseCases.Devices.DTOs;

public enum BorrowedDeviceStatus { OnLoan, PendingReturnConfirmation }

public sealed class BorrowedDeviceListQuery
{
    [StringLength(200)] public string? Search { get; init; }
    [StringLength(32)] public string? LoanCode { get; init; }
    [EnumDataType(typeof(BorrowedDeviceStatus))] public BorrowedDeviceStatus? Status { get; init; }
    public bool? Overdue { get; init; }
    [Range(1, 1_000_000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
}

// Loan-scoped view; it does not expose the owner's asset-management DTO or permissions.
public sealed record BorrowedDeviceDto(string DeviceCode, string Name, string CategoryCode, string CategoryName,
    string? SerialNumber, DeviceCondition Condition, string OwnerDepartmentCode, string OwnerDepartmentName,
    string? LenderDepartmentCode, string? LenderDepartmentName, string LoanCode, DateTimeOffset DueAt,
    DateTimeOffset? HandedOverAt, DateTimeOffset? ReturnRequestedAt, BorrowedDeviceStatus Status,
    bool IsOverdue, int WorkflowVersion);
