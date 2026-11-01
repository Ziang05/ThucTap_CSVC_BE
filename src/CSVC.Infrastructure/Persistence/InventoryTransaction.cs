using CSVC.Core.Devices;
using CSVC.UseCases.Abstractions.Persistence;
using CSVC.UseCases.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CSVC.Infrastructure.Persistence;

public sealed class InventoryTransaction(CsvcDbContext context) : IInventoryTransaction
{
    // Shared with account/session writers; inventory validation cannot race with role/session revocation.
    public const long InventoryAdvisoryLockKey = 0x43535643494E56;

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
    {
        await using var transaction = await BeginAsync(cancellationToken);
        try
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({AccountTransaction.AdvisoryLockKey})", cancellationToken);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({InventoryAdvisoryLockKey})", cancellationToken);
            var result = await action(cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (Exception exception)
        {
            try { await transaction.RollbackAsync(CancellationToken.None); }
            catch (NpgsqlException) { /* The disconnected transaction is disposed below. */ }
            context.ChangeTracker.Clear();
            if (exception is DbUpdateException { InnerException: PostgresException postgres })
            {
                if (postgres.SqlState == PostgresErrorCodes.UniqueViolation)
                {
                    var (code, message) = postgres.ConstraintName switch
                    {
                        "uq_devices_normalized_device_code" or "uq_device_identities_device_code" or "PK_device_identities" => ("DEVICE_CODE_CONFLICT", "The device code is already in use."),
                        "uq_devices_barcode" or "uq_device_identities_barcode" => ("BARCODE_CONFLICT", "The barcode is already in use."),
                        "device_operation_holds_pkey" => ("DEVICE_BUSY", "The device is already reserved by another operation."),
                        "uq_device_transfers_code" => ("TRANSFER_CODE_CONFLICT", "The transfer code is already in use."),
                        _ => ("DEVICE_OPERATION_CONFLICT", "The device or operation conflicts with an existing record.")
                    };
                    throw new UseCaseException(FailureKind.Conflict, code, message);
                }
                if (postgres.SqlState == PostgresErrorCodes.ForeignKeyViolation)
                    throw new UseCaseException(FailureKind.Conflict, "DEVICE_REFERENCE_CHANGED", "Related data changed. Reload and retry.");
            }
            if (exception is DbUpdateConcurrencyException)
                throw new UseCaseException(FailureKind.Conflict, "VERSION_CONFLICT", "Related data changed. Reload and retry.");
            if (exception is NpgsqlException or DbUpdateException or TimeoutException)
                throw new UseCaseException(FailureKind.Unavailable, "DEVICE_WORKFLOW_UNAVAILABLE", "The device workflow is temporarily unavailable.");
            throw;
        }
    }

    private async Task<IDbContextTransaction> BeginAsync(CancellationToken ct)
    {
        try { return await context.Database.BeginTransactionAsync(ct); }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException)
        {
            throw new UseCaseException(FailureKind.Unavailable, "DEVICE_WORKFLOW_UNAVAILABLE", "The device workflow is temporarily unavailable.");
        }
    }

    public async Task LockDevicesAsync(IEnumerable<Guid> deviceIds, CancellationToken cancellationToken = default)
    {
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Device row locks must be acquired inside an inventory transaction.");

        var ids = deviceIds.Distinct().Order().ToArray();
        if (ids.Length == 0)
            return;

        await context.Devices.FromSqlInterpolated($"""
            SELECT * FROM devices
            WHERE id = ANY ({ids})
            ORDER BY id
            FOR UPDATE
            """).AsNoTracking().ToListAsync(cancellationToken);
    }
}
