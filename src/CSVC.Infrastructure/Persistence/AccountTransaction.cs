using CSVC.UseCases.Abstractions.Persistence;
using CSVC.UseCases.Exceptions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CSVC.Infrastructure.Persistence;

public sealed class AccountTransaction(CsvcDbContext context) : IAccountTransaction
{
    // Every account/session/reset writer must use this lock to preserve cross-row invariants.
    public const long AdvisoryLockKey = 0x43535643414343;

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({AdvisoryLockKey})", cancellationToken);
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
                if (postgres.SqlState == PostgresErrorCodes.UniqueViolation)
                    throw new UseCaseException(FailureKind.Conflict, "ACCOUNT_CONFLICT", "Account data conflicts with an existing record.");
                if (postgres.SqlState == PostgresErrorCodes.ForeignKeyViolation)
                    throw new UseCaseException(FailureKind.Conflict, "ACCOUNT_REFERENCE_CHANGED", "Related data changed. Reload and retry.");
            }
            throw;
        }
    }

    public async Task LockDepartmentAsync(Guid departmentId, CancellationToken cancellationToken)
    {
        // Prevent a concurrent department lock/delete between eligibility validation and commit.
        await context.Departments.FromSqlInterpolated(
            $"SELECT * FROM departments WHERE id = {departmentId} FOR SHARE").AsNoTracking().ToListAsync(cancellationToken);
    }
}
