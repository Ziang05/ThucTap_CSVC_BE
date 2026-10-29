namespace CSVC.Core.Devices;

/// <summary>Globally reserves device codes and barcodes, including codes assigned to pending receipt items.</summary>
public sealed class DeviceIdentity
{
    public required string DeviceCode { get; set; }
    public required string Barcode { get; set; }
    public DateTimeOffset ReservedAt { get; set; }
}
