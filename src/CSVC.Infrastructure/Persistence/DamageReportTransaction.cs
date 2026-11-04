using CSVC.UseCases.Abstractions.Persistence;
using CSVC.UseCases.Exceptions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CSVC.Infrastructure.Persistence;

public sealed class DamageReportTransaction(CsvcDbContext db) : IDamageReportTransaction
{
    public bool CanDiscardFiles { get; private set; }
    private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginAsync(CancellationToken ct)
    {
        try { return await db.Database.BeginTransactionAsync(ct); }
        catch (Exception error) when (error is NpgsqlException or TimeoutException)
        { throw new UseCaseException(FailureKind.Unavailable, "REPORT_WRITE_UNAVAILABLE", "The report operation is temporarily unavailable."); }
    }

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        CanDiscardFiles = false;
        await using var transaction = await BeginAsync(ct);
        var committing = false;
        try
        {
            // Match the existing writer lock order; profile/role, settings, device moves and repair links cannot race validation.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({AccountTransaction.AdvisoryLockKey})", ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({InventoryTransaction.InventoryAdvisoryLockKey})", ct);
            var result = await action(ct);
            await db.SaveChangesAsync(ct);
            committing = true;
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (Exception error)
        {
            // A server rejection at COMMIT is definitive; a transport failure after COMMIT begins is ambiguous.
            CanDiscardFiles = !committing || error is PostgresException;
            try { await transaction.RollbackAsync(CancellationToken.None); }
            catch (Exception rollbackError) when (rollbackError is NpgsqlException or InvalidOperationException) { }
            db.ChangeTracker.Clear();
            var postgres = error as PostgresException ?? (error as DbUpdateException)?.InnerException as PostgresException;
            if (postgres?.SqlState is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation or PostgresErrorCodes.CheckViolation
                or PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected)
                throw new UseCaseException(FailureKind.Conflict, "REPORT_WRITE_CONFLICT", "Report data conflicts with another operation. Reload and retry.");
            if (error is DbUpdateConcurrencyException)
                throw new UseCaseException(FailureKind.Conflict, "REPORT_VERSION_CONFLICT", "The report changed. Reload and retry.");
            if (error is NpgsqlException or DbUpdateException or TimeoutException)
                throw new UseCaseException(FailureKind.Unavailable, "REPORT_WRITE_UNAVAILABLE", "The report operation is temporarily unavailable. Retry with the same requestId.");
            throw;
        }
    }
}
