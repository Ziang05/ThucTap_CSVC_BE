using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using CSVC.Core.Accounts;
using CSVC.Core.Authentication;
using CSVC.Core.Common;
using CSVC.Core.Departments;
using CSVC.Core.Areas;
using CSVC.Core.DeviceCategories;
using CSVC.Core.Suppliers;
using CSVC.Infrastructure.Persistence;
using CSVC.UseCases.Abstractions.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace CSVC.API.Tests;

internal sealed class AccountApiTestHost(string connectionString) : WebApplicationFactory<Program>
{
    public TestResetSender Sender { get; } = new();
    public FixedClock Clock { get; } = new();
    public AuditFailureInterceptor Failures { get; } = new();

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Database"] = connectionString,
            ["Authentication:ExpirationEnabled"] = "false"
        }));
        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IPasswordResetSender>(Sender);
            services.AddSingleton<TimeProvider>(Clock);
            services.AddDbContext<CsvcDbContext>(options => options.AddInterceptors(Failures));
        });
    }

    public async Task<(Guid Manager, string ManagerToken, Guid Staff, string StaffToken, Guid Department)> SeedAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
        await context.Database.MigrateAsync();
        var hash = scope.ServiceProvider.GetRequiredService<IAccountPasswordHasher>();
        var department = new Department { Code = "PB_TEST", Name = "Test department" };
        var managerProfile = new UserProfile { FullName = "Test Manager", DepartmentId = department.Id };
        var staffProfile = new UserProfile { FullName = "Test Staff", DepartmentId = department.Id };
        var manager = new Account { AccountCode = "TK_MANAGER", Email = "manager@example.com", RoleId = 1,
            UserProfileId = managerProfile.Id, PasswordHash = hash.Hash("OriginalPass123!") };
        var staff = new Account { AccountCode = "TK_STAFF", Email = "staff@example.com", RoleId = 2,
            UserProfileId = staffProfile.Id, PasswordHash = hash.Hash("OriginalPass123!") };
        var managerToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var staffToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        context.AddRange(department, managerProfile, staffProfile, manager, staff);
        context.AddRange(Session(manager.Id, managerToken), Session(staff.Id, staffToken));
        context.AccountHistories.Add(new AccountHistory
        {
            RequestId = Guid.NewGuid(), TargetAccountId = staff.Id, ActorAccountId = manager.Id,
            ActionCode = "ACCOUNT_CREATED", Outcome = AuditOutcome.Success,
            OccurredAt = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero)
        });
        await context.SaveChangesAsync();
        return (manager.Id, managerToken, staff.Id, staffToken, department.Id);
    }

    internal static AuthSession Session(Guid accountId, string token) => new()
    {
        AccountId = accountId,
        SessionTokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))),
        ExpiresAt = new DateTimeOffset(2026, 10, 22, 0, 0, 0, TimeSpan.Zero)
    };

    internal sealed class FixedClock : TimeProvider
    {
        private long _ticks = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero).Ticks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);
        public void Advance(TimeSpan amount) => Interlocked.Add(ref _ticks, amount.Ticks);
    }

    internal sealed class TestResetSender : IPasswordResetSender
    {
        public bool IsConfigured { get; set; } = true;
        public ConcurrentQueue<(string Email, string Token)> Messages { get; } = new();
        public Task SendAsync(string email, string token, DateTimeOffset expiresAt, CancellationToken cancellationToken)
        {
            Messages.Enqueue((email, token));
            return Task.CompletedTask;
        }
    }

    internal sealed class AuditFailureInterceptor : SaveChangesInterceptor
    {
        public bool FailNextAccountCreation { get; set; }
        public string? FailNextAccountAction { get; set; }
        public bool FailNextSessionCreation { get; set; }
        public bool FailNextLoginFailure { get; set; }
        public string? FailNextDepartmentAction { get; set; }
        public string? FailNextAreaAction { get; set; }
        public string? FailNextCategoryAction { get; set; }
        public string? FailNextSupplierAction { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (FailNextSupplierAction is { } supplierAction && eventData.Context!.ChangeTracker.Entries<SupplierHistory>()
                    .Any(entry => entry.State == EntityState.Added && entry.Entity.ActionCode == supplierAction))
            {
                FailNextSupplierAction = null;
                throw new InvalidOperationException("Simulated supplier audit storage failure.");
            }
            if (FailNextCategoryAction is { } categoryAction && eventData.Context!.ChangeTracker.Entries<CategoryHistory>()
                    .Any(entry => entry.State == EntityState.Added && entry.Entity.ActionCode == categoryAction))
            {
                FailNextCategoryAction = null;
                throw new InvalidOperationException("Simulated category audit storage failure.");
            }
            if (FailNextAccountAction is { } accountAction && eventData.Context!.ChangeTracker.Entries<AccountHistory>()
                    .Any(entry => entry.State == EntityState.Added && entry.Entity.ActionCode == accountAction))
            {
                FailNextAccountAction = null;
                throw new InvalidOperationException("Simulated account audit storage failure.");
            }
            if (FailNextAreaAction is { } areaAction && eventData.Context!.ChangeTracker.Entries<AreaHistory>()
                    .Any(entry => entry.State == EntityState.Added && entry.Entity.ActionCode == areaAction))
            {
                FailNextAreaAction = null;
                throw new InvalidOperationException("Simulated area audit storage failure.");
            }
            if (FailNextDepartmentAction is { } action && eventData.Context!.ChangeTracker.Entries<DepartmentHistory>()
                    .Any(entry => entry.State == EntityState.Added && entry.Entity.ActionCode == action))
            {
                FailNextDepartmentAction = null;
                throw new InvalidOperationException("Simulated department audit storage failure.");
            }
            if (FailNextAccountCreation && eventData.Context!.ChangeTracker.Entries<AccountHistory>()
                    .Any(entry => entry.State == EntityState.Added && entry.Entity.ActionCode == "ACCOUNT_CREATED"))
            {
                FailNextAccountCreation = false;
                throw new InvalidOperationException("Simulated audit storage failure.");
            }
            var actions = eventData.Context!.ChangeTracker.Entries<AuthHistory>()
                .Where(entry => entry.State == EntityState.Added).Select(entry => entry.Entity.ActionCode).ToArray();
            if (FailNextSessionCreation && actions.Contains("SESSION_CREATED"))
            {
                FailNextSessionCreation = false;
                throw new InvalidOperationException("Simulated session audit failure.");
            }
            if (FailNextLoginFailure && actions.Contains("LOGIN_FAILED"))
            {
                FailNextLoginFailure = false;
                throw new InvalidOperationException("Simulated failed-login audit failure.");
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}

internal sealed class AccountTestDatabase(string adminConnectionString, string name) : IAsyncDisposable
{
    public string ConnectionString { get; } = new NpgsqlConnectionStringBuilder(adminConnectionString)
    {
        Database = name, Pooling = false
    }.ConnectionString;

    public static async Task<AccountTestDatabase> CreateAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("CSVC_POSTGRES_TEST_CONNECTION")!;
        var name = "csvc_accounts_test_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
        await command.ExecuteNonQueryAsync();
        return new(connectionString, name);
    }

    public async ValueTask DisposeAsync()
    {
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE \"{name}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }
}
