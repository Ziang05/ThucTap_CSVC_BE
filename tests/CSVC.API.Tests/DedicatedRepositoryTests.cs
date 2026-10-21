using CSVC.Core.Accounts;
using CSVC.Core.Areas;
using CSVC.Core.Authentication;
using CSVC.Core.Common;
using CSVC.Core.Departments;
using CSVC.Infrastructure;
using CSVC.Infrastructure.Persistence;
using CSVC.UseCases.Abstractions.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CSVC.API.Tests;

public sealed class DedicatedRepositoryTests
{
    [Fact]
    public async Task Dedicated_repositories_are_the_same_scoped_instances_exposed_by_unit_of_work()
    {
        var options = CreateOptions();
        await using var provider = CreateProvider(options);
        await using var scope = provider.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        foreach (var property in typeof(IUnitOfWork).GetProperties())
        {
            Assert.Same(scope.ServiceProvider.GetRequiredService(property.PropertyType), property.GetValue(unitOfWork));
        }

        var department = new Department { Code = "PB01", Name = " Equipment\t Management  " };
        await unitOfWork.Departments.AddAsync(department);
        await unitOfWork.DepartmentHistories.AddAsync(new DepartmentHistory
        {
            RequestId = Guid.NewGuid(),
            DepartmentId = department.Id,
            ActionCode = "DEPARTMENT_CREATED",
            Outcome = AuditOutcome.Success,
            OccurredAt = DateTimeOffset.UtcNow
        });
        var directRepository = scope.ServiceProvider.GetRequiredService<IDepartmentRepository>();
        Assert.Same(department, await directRepository.GetByIdAsync(department.Id));
        await using var reader = new CsvcDbContext(options);
        Assert.Empty(await reader.Departments.ToListAsync());
        Assert.Empty(await reader.DepartmentHistories.ToListAsync());

        Assert.Equal(2, await unitOfWork.CommitAsync());
        Assert.Equal("PB01", (await reader.Departments.SingleAsync()).Code);
        Assert.Equal("Equipment Management", (await reader.Departments.SingleAsync()).Name);
        Assert.Equal(department.Id, (await reader.DepartmentHistories.SingleAsync()).DepartmentId);
        Assert.NotNull(await unitOfWork.Departments.GetByCodeAsync("PB01"));
        Assert.Null(await unitOfWork.Departments.GetByCodeAsync("MISSING"));
    }

    [Fact]
    public async Task Account_lookup_uses_normalized_email_and_returns_an_untracked_entity()
    {
        await using var provider = CreateProvider(CreateOptions());
        await using var scope = provider.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var account = new Account
        {
            AccountCode = "TK01",
            UserProfileId = Guid.NewGuid(),
            RoleId = 1,
            Email = " User@Example.COM ",
            PasswordHash = "test-hash"
        };
        await unitOfWork.Accounts.AddAsync(account);
        await unitOfWork.CommitAsync();
        var context = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
        context.ChangeTracker.Clear();

        var result = await unitOfWork.Accounts.GetByNormalizedEmailAsync("\tUSER@example.com ");
        Assert.NotNull(result);
        Assert.Equal(account.Id, result.Id);
        Assert.Equal("user@example.com", result.Email);
        Assert.Null(await unitOfWork.Accounts.GetByNormalizedEmailAsync("missing@example.com"));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Area_repository_distinguishes_root_nodes_and_direct_children()
    {
        await using var provider = CreateProvider(CreateOptions());
        await using var scope = provider.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var root = new Area { Code = "A", Name = "Building A" };
        var child = new Area { Code = "A1", Name = "Floor 1", ParentId = root.Id };
        var grandchild = new Area { Code = "A101", Name = "Room 101", ParentId = child.Id };
        await unitOfWork.Areas.AddRangeAsync([root, child, grandchild]);
        await unitOfWork.CommitAsync();

        Assert.Equal(root.Id, Assert.Single(await unitOfWork.Areas.GetChildrenAsync(null)).Id);
        Assert.Equal(child.Id, Assert.Single(await unitOfWork.Areas.GetChildrenAsync(root.Id)).Id);
        Assert.Equal(grandchild.Id, Assert.Single(await unitOfWork.Areas.GetChildrenAsync(child.Id)).Id);
        Assert.Empty(await unitOfWork.Areas.GetChildrenAsync(grandchild.Id));
    }

    [Fact]
    public async Task Active_session_lookup_excludes_expired_revoked_and_other_accounts_sessions()
    {
        await using var provider = CreateProvider(CreateOptions());
        await using var scope = provider.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var accountId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var active = new AuthSession
        {
            AccountId = accountId,
            SessionTokenHash = "active",
            ExpiresAt = now.AddMinutes(10)
        };
        await unitOfWork.AuthSessions.AddRangeAsync([
            active,
            new AuthSession { AccountId = accountId, SessionTokenHash = "expired", ExpiresAt = now },
            new AuthSession { AccountId = accountId, SessionTokenHash = "revoked", ExpiresAt = now.AddMinutes(10), IsActive = false },
            new AuthSession { AccountId = Guid.NewGuid(), SessionTokenHash = "other", ExpiresAt = now.AddMinutes(10) }
        ]);
        await unitOfWork.CommitAsync();

        Assert.Equal(active.Id, (await unitOfWork.AuthSessions.GetActiveByAccountIdAsync(accountId, now))?.Id);
        Assert.Null(await unitOfWork.AuthSessions.GetActiveByAccountIdAsync(accountId, now.AddMinutes(10)));
    }

    [Fact]
    public async Task PostgreSql_model_generates_schema_for_all_twelve_dbml_tables_and_native_enum_labels()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(CreateConfiguration());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
        var script = context.Database.GenerateCreateScript();

        Assert.Equal(12, context.Model.GetEntityTypes().Count());
        Assert.Contains("CREATE TYPE entity_status AS ENUM ('ACTIVE', 'LOCKED')", script);
        Assert.Contains("'NO_CHANGE'", script);
        Assert.Contains("before_data jsonb", script);
        Assert.Contains("ip_address inet", script);
        Assert.Contains("uq_auth_history_session_ended", script);
        foreach (var entity in context.Model.GetEntityTypes())
        {
            Assert.NotNull(entity.FindPrimaryKey());
            Assert.All(entity.GetForeignKeys(), key => Assert.Equal(DeleteBehavior.Restrict, key.DeleteBehavior));
        }
    }

    private static DbContextOptions<CsvcDbContext> CreateOptions()
        => new DbContextOptionsBuilder<CsvcDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    private static IConfiguration CreateConfiguration()
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Database"] = "Host=localhost;Database=test;Username=test"
        }).Build();

    private static ServiceProvider CreateProvider(DbContextOptions<CsvcDbContext> options)
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(CreateConfiguration());
        services.Replace(ServiceDescriptor.Scoped<CsvcDbContext>(_ => new CsvcDbContext(options)));
        return services.BuildServiceProvider();
    }
}
