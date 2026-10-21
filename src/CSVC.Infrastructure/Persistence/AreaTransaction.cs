using System.Data;
using CSVC.UseCases.Abstractions.Persistence;
using CSVC.UseCases.Exceptions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CSVC.Infrastructure.Persistence;

public sealed class AreaTransaction(CsvcDbContext context) : IAreaTransaction
{
    // These values match csvc_lock_area_tree() in the existing migration.
    public const int TreeLockNamespace = 1129535043;
    public const int TreeLockId = 1;

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            // Fixed order: protect actor/session eligibility, then the whole tree before reading it.
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({AccountTransaction.AdvisoryLockKey})", cancellationToken);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({TreeLockNamespace}, {TreeLockId})", cancellationToken);
            var result = await action(cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            if (exception is DbUpdateException { InnerException: PostgresException postgres })
            {
                if (postgres.SqlState == PostgresErrorCodes.UniqueViolation
                    && postgres.ConstraintName is "uq_areas_root_normalized_name" or "uq_areas_sibling_normalized_name")
                    throw new UseCaseException(FailureKind.Conflict, "AREA_NAME_CONFLICT", "An area with this name already exists under the selected parent.");
                if (postgres.SqlState == PostgresErrorCodes.CheckViolation
                    && postgres.ConstraintName is "ck_areas_no_cycle" or "ck_areas_not_self_parent")
                    throw new UseCaseException(FailureKind.Conflict, "AREA_CYCLE", "The selected parent would create a cycle.");
                if (postgres.SqlState == PostgresErrorCodes.ForeignKeyViolation)
                    throw new UseCaseException(FailureKind.Conflict, "AREA_REFERENCE_CHANGED", "Related data changed. Reload and retry.");
            }
            throw;
        }
    }
}
