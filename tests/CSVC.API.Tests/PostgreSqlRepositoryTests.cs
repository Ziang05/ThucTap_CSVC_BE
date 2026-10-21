using System.Net;
using CSVC.Core.Accounts;
using CSVC.Core.Areas;
using CSVC.Core.Authentication;
using CSVC.Core.Common;
using CSVC.Core.Departments;
using CSVC.Infrastructure;
using CSVC.Infrastructure.Persistence;
using CSVC.UseCases.Abstractions.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CSVC.API.Tests;

public sealed class PostgreSqlRepositoryTests
{
    [PostgreSqlFact]
    public async Task Typed_repositories_persist_native_types_and_failed_audit_rolls_back_the_commit()
    {
        // Always create a uniquely named disposable database; never change the configured application DB.
        var adminConnectionString = Environment.GetEnvironmentVariable("CSVC_POSTGRES_TEST_CONNECTION")!;
        var databaseName = "csvc_repository_test_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(adminConnectionString);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin))
        {
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            var connectionString = new NpgsqlConnectionStringBuilder(adminConnectionString)
            {
                Database = databaseName,
                Pooling = false
            }.ConnectionString;
            var services = new ServiceCollection();
            services.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:Database"] = connectionString }).Build());
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
            await context.Database.MigrateAsync();
            Assert.Empty(await context.Database.GetPendingMigrationsAsync());
            Assert.Equal(3, await context.Roles.CountAsync());
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var department = new Department { Code = "PB01", Name = "Equipment" };
            var profile = new UserProfile { DepartmentId = department.Id, FullName = "Test User", BirthDate = new DateOnly(2000, 1, 1) };
            var account = new Account
            {
                AccountCode = "TK01",
                UserProfileId = profile.Id,
                RoleId = 1,
                Email = "user@example.com",
                PasswordHash = "test-hash"
            };
            await unitOfWork.Departments.AddAsync(department);
            await unitOfWork.UserProfiles.AddAsync(profile);
            await unitOfWork.Accounts.AddAsync(account);
            Assert.Equal(3, await unitOfWork.CommitAsync());
            Assert.Equal(EntityStatus.Active, (await unitOfWork.Departments.GetByCodeAsync("PB01"))?.Status);
            Assert.Equal(account.Id, (await unitOfWork.Accounts.GetByNormalizedEmailAsync("user@example.com"))?.Id);

            var now = DateTimeOffset.UtcNow;
            var session = new AuthSession { AccountId = account.Id, SessionTokenHash = "test-token-hash", ExpiresAt = now.AddMinutes(10) };
            var root = new Area { Code = "A", Name = "Building A" };
            var child = new Area { Code = "A1", Name = "Floor 1", ParentId = root.Id };
            await unitOfWork.AuthSessions.AddAsync(session);
            await unitOfWork.Areas.AddRangeAsync([root, child]);
            await unitOfWork.CommitAsync();
            Assert.Equal(session.Id, (await unitOfWork.AuthSessions.GetActiveByAccountIdAsync(account.Id, now))?.Id);
            Assert.Equal(child.Id, Assert.Single(await unitOfWork.Areas.GetChildrenAsync(root.Id)).Id);

            await PostgreSqlConstraintChecks.VerifyAsync(connectionString, department.Id, account.Id, root.Id, child.Id);

            session.IsActive = false;
            await unitOfWork.AuthHistories.AddAsync(new AuthHistory
            {
                RequestId = Guid.NewGuid(),
                ActorAccountId = account.Id,
                AccountId = account.Id,
                SessionId = session.Id,
                ActionCode = "SESSION_ENDED",
                Outcome = AuditOutcome.Success,
                OccurredAt = now,
                IpAddress = IPAddress.Loopback
            });
            await unitOfWork.DepartmentHistories.AddAsync(new DepartmentHistory
            {
                RequestId = Guid.NewGuid(),
                ActorAccountId = account.Id,
                DepartmentId = department.Id,
                ActionCode = "DEPARTMENT_CREATED",
                Outcome = AuditOutcome.Success,
                OccurredAt = now,
                AfterData = "{\"name\":\"Equipment\"}"
            });
            await unitOfWork.CommitAsync();
            context.ChangeTracker.Clear();
            Assert.Equal(IPAddress.Loopback, (await unitOfWork.AuthHistories.GetAllAsync()).Single().IpAddress);
            Assert.Equal(AuditOutcome.Success, (await unitOfWork.AuthHistories.GetAllAsync()).Single().Outcome);
            Assert.Contains("Equipment", (await unitOfWork.DepartmentHistories.GetAllAsync()).Single().AfterData!);
            Assert.Null(await unitOfWork.AuthSessions.GetActiveByAccountIdAsync(account.Id, now));

            var trackedDepartment = await unitOfWork.Departments.GetByIdAsync(department.Id);
            Assert.NotNull(trackedDepartment);
            trackedDepartment.Name = "Must roll back";
            await unitOfWork.AuthHistories.AddAsync(new AuthHistory
            {
                RequestId = Guid.NewGuid(),
                SessionId = session.Id,
                ActionCode = "SESSION_ENDED",
                Outcome = AuditOutcome.Success,
                OccurredAt = now
            });
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => unitOfWork.CommitAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
            await using var readerScope = provider.CreateAsyncScope();
            var reader = readerScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            Assert.Equal("Equipment", (await reader.Departments.GetByCodeAsync("PB01"))?.Name);
            Assert.Single(await reader.AuthHistories.GetAllAsync());

            // Verify migration rollback/reapply only in this disposable database.
            var migrationContext = readerScope.ServiceProvider.GetRequiredService<CsvcDbContext>();
            var migrator = migrationContext.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
            await migrator.MigrateAsync("0");
            Assert.Empty(await migrationContext.Database.GetAppliedMigrationsAsync());
            await migrationContext.Database.MigrateAsync();
            Assert.Equal(3, await migrationContext.Roles.CountAsync());
        }
        finally
        {
            // databaseName is generated locally above and cannot name the user's application database.
            await using var drop = new NpgsqlCommand($"DROP DATABASE \"{databaseName}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}

public sealed class PostgreSqlFactAttribute : FactAttribute
{
    public PostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CSVC_POSTGRES_TEST_CONNECTION")))
        {
            Skip = "Set CSVC_POSTGRES_TEST_CONNECTION to a PostgreSQL connection with permission to create disposable test databases.";
        }
    }
}
