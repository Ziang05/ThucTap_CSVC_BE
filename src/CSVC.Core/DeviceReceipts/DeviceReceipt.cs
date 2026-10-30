namespace CSVC.Core.DeviceReceipts;

public enum DeviceReceiptStatus
{
    PendingApproval,
    Rejected,
    Approved,
    Completed,
    Cancelled
}

public sealed class DeviceReceipt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string ReceiptCode { get; set; }
    public Guid SupplierId { get; set; }
    public string? CarrierName { get; set; }
    public string? RequestCode { get; set; }
    public DateOnly DeliveryDate { get; set; }
    public required string DeliveryBatch { get; set; }
    public Guid RequestingDepartmentId { get; set; }
    public string? PaidInvoiceCode { get; set; }
    public Guid CreatorAccountId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DeviceReceiptStatus Status { get; set; } = DeviceReceiptStatus.PendingApproval;
    public Guid? DecisionAccountId { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? DecisionReason { get; set; }
    public string? Notes { get; set; }
    public int Version { get; set; } = 1;
    public ICollection<DeviceReceiptItem> Items { get; set; } = new List<DeviceReceiptItem>();
}

public sealed class DeviceReceiptItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ReceiptId { get; set; }
    public Guid CategoryId { get; set; }
    public required string Name { get; set; }
    public required string DeviceCode { get; set; }
    public required string Barcode { get; set; }
    public CSVC.Core.Devices.DeviceCodeSource DeviceCodeSource { get; set; }
    public string? SerialNumber { get; set; }
    public string? ParentDeviceCode { get; set; }
    public Guid? DeviceId { get; set; }
}
