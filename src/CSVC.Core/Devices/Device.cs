using CSVC.Core.Common;

namespace CSVC.Core.Devices;

public sealed class Device
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string DeviceCode { get; set; }
    public required string Barcode { get; set; }
    public required string Name { get; set; }
    public Guid CategoryId { get; set; }
    public Guid? ManufacturerId { get; set; }
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }
    public DateOnly? PurchaseDate { get; set; }
    public string? PaidInvoiceCode { get; set; }
    public decimal? PurchasePrice { get; set; }
    public DateOnly? WarrantyExpiryDate { get; set; }
    public Guid DepartmentId { get; set; }
    public Guid? AreaId { get; set; }
    public Guid? ResponsibleAccountId { get; set; }
    public Guid? ParentDeviceId { get; set; }
    public EntityStatus Status { get; set; } = EntityStatus.Active;
    public DeviceCondition Condition { get; set; } = DeviceCondition.Good;
    public string? Notes { get; set; }
    public int Version { get; set; } = 1;
}
