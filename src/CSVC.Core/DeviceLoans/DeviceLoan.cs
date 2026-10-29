using CSVC.Core.Devices;

namespace CSVC.Core.DeviceLoans;

public enum DeviceLoanStatus
{
    PendingApproval,
    Rejected,
    Approved,
    OnLoan,
    Returned,
    Cancelled
}

public sealed class DeviceLoan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string LoanCode { get; set; }
    public Guid BorrowerAccountId { get; set; }
    public Guid BorrowerDepartmentId { get; set; }
    public required string Purpose { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public Guid CreatorAccountId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DeviceLoanStatus Status { get; set; } = DeviceLoanStatus.PendingApproval;
    public Guid? DecisionAccountId { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public DateTimeOffset? HandedOverAt { get; set; }
    public Guid? HandoverAccountId { get; set; }
    public DateTimeOffset? ReturnedAt { get; set; }
    public Guid? ReturnAccountId { get; set; }
    public string? DecisionReason { get; set; }
    public string? Notes { get; set; }
    public int Version { get; set; } = 1;
    public ICollection<DeviceLoanItem> Items { get; set; } = new List<DeviceLoanItem>();
}

public sealed class DeviceLoanItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LoanId { get; set; }
    public Guid DeviceId { get; set; }
    public DeviceCondition? ConditionAtHandover { get; set; }
    public string? HandoverNote { get; set; }
    public DeviceCondition? ConditionAtReturn { get; set; }
    public string? ReturnNote { get; set; }
}
