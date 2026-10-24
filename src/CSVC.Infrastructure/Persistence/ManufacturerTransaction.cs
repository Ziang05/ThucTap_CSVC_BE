using CSVC.UseCases.Abstractions.Persistence;
using CSVC.UseCases.Exceptions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CSVC.Infrastructure.Persistence;

public sealed class ManufacturerTransaction(CsvcDbContext context) : IManufacturerTransaction
{
    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Serialize with account writers so session and role checks cannot race with revocation.
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({AccountTransaction.AdvisoryLockKey})", cancellationToken);
            var result = await action(cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            if (exception is DbUpdateException { InnerException: PostgresException
                { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "uq_manufacturers_normalized_display_code" or "IX_manufacturers_display_code" } })
                throw new UseCaseException(FailureKind.Conflict, "MANUFACTURER_CODE_CONFLICT", "A manufacturer with this display code already exists.");
            if (exception is DbUpdateException { InnerException: PostgresException
                { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "uq_manufacturers_normalized_name" } })
                throw new UseCaseException(FailureKind.Conflict, "MANUFACTURER_NAME_CONFLICT", "A manufacturer with this name already exists.");
            throw;
        }
    }
}
