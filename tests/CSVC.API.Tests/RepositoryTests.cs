using CSVC.Infrastructure;
using CSVC.Infrastructure.Persistence;
using CSVC.Infrastructure.Persistence.Repositories;
using CSVC.UseCases.Abstractions.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CSVC.API.Tests;

// InMemory verifies repository tracking/commit behavior, not PostgreSQL constraints or transactions.
public sealed class RepositoryTests
{
    [Fact]
    public async Task Injected_repository_and_unit_of_work_share_pending_changes_and_commit_together()
    {
        var options = CreateOptions();
        var services = new ServiceCollection();
        services.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = "Host=localhost;Database=test;Username=test"
            }).Build());
        services.Replace(ServiceDescriptor.Scoped<CsvcDbContext>(_ => new RepositoryTestContext(options)));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<Probe>>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var entity = new Probe { Name = "Department" };

        await repository.AddAsync(entity);
        var auditRepository = scope.ServiceProvider.GetRequiredService<IRepository<AuditProbe>>();
        await auditRepository.AddAsync(new AuditProbe { Action = "CREATED" });
        Assert.Same(unitOfWork.Accounts, scope.ServiceProvider.GetRequiredService<IAccountRepository>());
        Assert.Same(entity, await repository.GetByIdAsync(entity.Id));
        await using var reader = new RepositoryTestContext(options);
        Assert.Empty(await reader.Set<Probe>().ToListAsync());
        Assert.Empty(await reader.Set<AuditProbe>().ToListAsync());

        Assert.Equal(2, await unitOfWork.CommitAsync());
        Assert.Equal("Department", (await reader.Set<Probe>().SingleAsync()).Name);
        Assert.Equal("CREATED", (await reader.Set<AuditProbe>().SingleAsync()).Action);
    }

    [Fact]
    public async Task Read_queries_are_untracked_and_tracked_queries_persist_changes_on_commit()
    {
        var options = CreateOptions();
        await using var context = new RepositoryTestContext(options);
        var unitOfWork = CreateUnitOfWork(context);
        var repository = new GenericRepository<Probe>(context);
        await repository.AddRangeAsync([new Probe { Name = "Alpha" }, new Probe { Name = "Beta" }]);
        await unitOfWork.CommitAsync();
        context.ChangeTracker.Clear();

        Assert.Equal(2, (await repository.GetAllAsync()).Count);
        Assert.Single(await repository.FindAsync(entity => entity.Name == "Alpha"));
        var detached = await repository.FirstOrDefaultAsync(entity => entity.Name == "Alpha");
        Assert.True(await repository.ExistsAsync(entity => entity.Name == "Beta"));
        Assert.False(await repository.ExistsAsync(entity => entity.Name == "Missing"));
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.NotNull(detached);
        detached.Name = "Not persisted";
        Assert.Equal(0, await unitOfWork.CommitAsync());

        var tracked = await repository.FirstOrDefaultTrackedAsync(entity => entity.Name == "Alpha");
        Assert.NotNull(tracked);
        tracked.Name = "Updated";
        Assert.Equal(1, await unitOfWork.CommitAsync());
        await using var reader = new RepositoryTestContext(options);
        Assert.True(await reader.Set<Probe>().AnyAsync(entity => entity.Name == "Updated"));
        Assert.False(await reader.Set<Probe>().AnyAsync(entity => entity.Name == "Not persisted"));
    }

    [Fact]
    public async Task Detached_updates_and_deletes_are_only_persisted_at_commit()
    {
        var options = CreateOptions();
        await using var context = new RepositoryTestContext(options);
        var unitOfWork = CreateUnitOfWork(context);
        var repository = new GenericRepository<Probe>(context);
        await repository.AddRangeAsync([
            new Probe { Name = "Alpha" }, new Probe { Name = "Beta" }, new Probe { Name = "Gamma" }
        ]);
        await unitOfWork.CommitAsync();
        context.ChangeTracker.Clear();

        var items = await repository.GetAllAsync();
        items[0].Name = "Updated";
        repository.Update(items[0]);
        await repository.DeleteAsync(items[1]);
        await repository.DeleteRangeAsync([items[2]]);
        await using var reader = new RepositoryTestContext(options);
        Assert.Equal(3, await reader.Set<Probe>().CountAsync());
        Assert.False(await reader.Set<Probe>().AnyAsync(entity => entity.Name == "Updated"));

        Assert.Equal(3, await unitOfWork.CommitAsync());
        Assert.Equal("Updated", (await reader.Set<Probe>().AsNoTracking().SingleAsync()).Name);
    }

    [Fact]
    public async Task Missing_records_return_null_and_cancelled_commit_does_not_persist_pending_changes()
    {
        var options = CreateOptions();
        await using var context = new RepositoryTestContext(options);
        var unitOfWork = CreateUnitOfWork(context);
        var repository = new GenericRepository<Probe>(context);
        Assert.Null(await repository.GetByIdAsync(Guid.NewGuid()));
        Assert.Null(await repository.FirstOrDefaultAsync(entity => entity.Name == "Missing"));
        await repository.AddAsync(new Probe { Name = "Pending" });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => unitOfWork.CommitAsync(cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.GetAllAsync(cancellation.Token));
        await using var reader = new RepositoryTestContext(options);
        Assert.Empty(await reader.Set<Probe>().ToListAsync());
    }

    private static UnitOfWork CreateUnitOfWork(CsvcDbContext context)
        => new(context,
            new RoleRepository(context),
            new DepartmentRepository(context),
            new DepartmentHistoryRepository(context),
            new UserProfileRepository(context),
            new AccountRepository(context, new AutoMapper.MapperConfiguration(configuration =>
                configuration.AddProfile<UseCases.Accounts.Mapping.AccountMappingProfile>(),
                Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance).CreateMapper()),
            new AccountHistoryRepository(context),
            new AuthSessionRepository(context),
            new AuthHistoryRepository(context),
            new AuthLoginThrottleRepository(context),
            new PasswordResetRequestRepository(context),
            new AreaRepository(context),
            new AreaHistoryRepository(context));

    private static DbContextOptions<CsvcDbContext> CreateOptions()
        => new DbContextOptionsBuilder<CsvcDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    private sealed class RepositoryTestContext(DbContextOptions<CsvcDbContext> options) : CsvcDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Probe>().HasKey(entity => entity.Id);
            modelBuilder.Entity<AuditProbe>().HasKey(entity => entity.Id);
        }
    }

    public sealed class Probe
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
    }

    public sealed class AuditProbe
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Action { get; set; } = string.Empty;
    }
}
