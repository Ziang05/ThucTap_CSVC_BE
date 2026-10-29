using CSVC.Core.Devices;
using CSVC.UseCases.Abstractions.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CSVC.Infrastructure.Persistence.Repositories;

public sealed class DeviceOperationHoldRepository(CsvcDbContext context) : IDeviceOperationHoldRepository
{
    public Task<bool> ExistsForDeviceAsync(Guid deviceId, CancellationToken cancellationToken = default)
        => context.DeviceOperationHolds.AsNoTracking().AnyAsync(x => x.DeviceId == deviceId, cancellationToken);

    public Task<bool> IsOwnedByAsync(string deviceCode, DeviceOperationKind operationKind, Guid operationId, CancellationToken cancellationToken = default)
    {
        var normalizedCode = deviceCode.Trim().ToUpperInvariant();
        return context.DeviceOperationHolds.AsNoTracking().AnyAsync(x => x.DeviceCode == normalizedCode
            && x.OperationKind == operationKind && x.OperationId == operationId, cancellationToken);
    }

    public async Task<bool> TryAcquireAsync(DeviceOperationHold hold, CancellationToken cancellationToken = default)
    {
        var normalizedCode = hold.DeviceCode.Trim().ToUpperInvariant();
        var rows = await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO device_operation_holds (device_code, device_id, operation_kind, operation_id, held_at)
            VALUES ({normalizedCode}, {hold.DeviceId}, {hold.OperationKind.ToString()}, {hold.OperationId}, {hold.HeldAt})
            ON CONFLICT (device_code) DO NOTHING
            """, cancellationToken);
        return rows == 1;
    }

    public async Task<bool> ReleaseAsync(string deviceCode, DeviceOperationKind operationKind, Guid operationId,
        CancellationToken cancellationToken = default)
    {
        var normalizedCode = deviceCode.Trim().ToUpperInvariant();
        return await context.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM device_operation_holds
            WHERE device_code = {normalizedCode} AND operation_kind = {operationKind.ToString()} AND operation_id = {operationId}
            """, cancellationToken) == 1;
    }

    public Task<int> AttachDeviceAsync(string deviceCode, Guid deviceId, DeviceOperationKind operationKind, Guid operationId,
        CancellationToken cancellationToken = default)
    {
        var normalizedCode = deviceCode.Trim().ToUpperInvariant();
        return context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE device_operation_holds SET device_id = {deviceId}
            WHERE device_code = {normalizedCode} AND operation_kind = {operationKind.ToString()} AND operation_id = {operationId}
            """, cancellationToken);
    }
}
