using CSVC.Core.Devices;
using CSVC.Core.ServiceOrders;
using CSVC.UseCases.Abstractions.Persistence;
using CSVC.UseCases.Exceptions;

namespace CSVC.UseCases.Devices;

public interface IDeviceReservationPolicy
{
    Task RequireAsync(Device device, bool allowServiceHold, DeviceOperationKind? ownerKind = null,
        Guid? ownerId = null, CancellationToken ct = default);
}

public sealed class DeviceReservationPolicy(IUnitOfWork uow, IRepository<ServiceOrderDeviceHold> serviceHolds)
    : IDeviceReservationPolicy
{
    public async Task RequireAsync(Device device, bool allowServiceHold, DeviceOperationKind? ownerKind = null,
        Guid? ownerId = null, CancellationToken ct = default)
    {
        var operationValid = ownerKind is { } kind && ownerId is { } id
            ? await uow.DeviceOperationHolds.IsOwnedByAsync(device.DeviceCode, kind, id, ct)
            : !await uow.DeviceOperationHolds.ExistsForDeviceAsync(device.Id, ct);
        if (!operationValid || (!allowServiceHold && await serviceHolds.ExistsAsync(h => h.DeviceId == device.Id, ct)))
            throw new UseCaseException(FailureKind.Conflict, "DEVICE_BUSY", "The device is reserved or its reservation no longer belongs to this document.");
    }
}
