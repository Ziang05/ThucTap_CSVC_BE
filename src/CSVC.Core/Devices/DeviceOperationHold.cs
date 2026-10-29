namespace CSVC.Core.Devices;

/// <summary>Exclusive reservation for a device code across receipt, loan and transfer workflows.</summary>
public sealed class DeviceOperationHold
{
    public required string DeviceCode { get; set; }
    public Guid? DeviceId { get; set; }
    public DeviceOperationKind OperationKind { get; set; }
    public Guid OperationId { get; set; }
    public required DateTimeOffset HeldAt { get; set; }
}
