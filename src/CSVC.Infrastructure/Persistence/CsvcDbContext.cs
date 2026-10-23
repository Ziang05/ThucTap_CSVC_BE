using CSVC.Core.Accounts;
using CSVC.Core.Areas;
using CSVC.Core.Authentication;
using CSVC.Core.Common;
using CSVC.Core.Departments;
using CSVC.Core.DeviceCategories;
using CSVC.Core.Suppliers;
using CSVC.Core.Manufacturers;
using Microsoft.EntityFrameworkCore;

namespace CSVC.Infrastructure.Persistence;

public class CsvcDbContext(DbContextOptions<CsvcDbContext> options)
    : DbContext(options)
{
    public DbSet<Role> Roles => Set<Role>();

    public DbSet<Department> Departments => Set<Department>();

    public DbSet<DepartmentHistory> DepartmentHistories => Set<DepartmentHistory>();

    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<AccountHistory> AccountHistories => Set<AccountHistory>();

    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();

    public DbSet<AuthHistory> AuthHistories => Set<AuthHistory>();

    public DbSet<AuthLoginThrottle> AuthLoginThrottles => Set<AuthLoginThrottle>();

    public DbSet<PasswordResetRequest> PasswordResetRequests => Set<PasswordResetRequest>();

    public DbSet<Area> Areas => Set<Area>();

    public DbSet<AreaHistory> AreaHistories => Set<AreaHistory>();

    public DbSet<DeviceCategory> DeviceCategories => Set<DeviceCategory>();

    public DbSet<CategoryHistory> CategoryHistories => Set<CategoryHistory>();

    public DbSet<Supplier> Suppliers => Set<Supplier>();

    public DbSet<SupplierHistory> SupplierHistories => Set<SupplierHistory>();

    public DbSet<Manufacturer> Manufacturers => Set<Manufacturer>();

    public DbSet<ManufacturerHistory> ManufacturerHistories => Set<ManufacturerHistory>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        NormalizeChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        NormalizeChanges();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void NormalizeChanges()
    {
        foreach (var entry in ChangeTracker.Entries().Where(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            switch (entry.Entity)
            {
                case Account account:
                    entry.Property("NormalizedCode").CurrentValue = account.AccountCode.Trim().ToLowerInvariant();
                    account.Email = TextNormalization.Email(account.Email);
                    entry.Property("NormalizedEmail").CurrentValue = account.Email;
                    break;
                case Department department:
                    entry.Property("NormalizedCode").CurrentValue = department.Code.Trim().ToLowerInvariant();
                    department.Name = TextNormalization.Name(department.Name);
                    entry.Property("NormalizedName").CurrentValue = department.Name.ToLowerInvariant();
                    break;
                case Area area:
                    area.Name = TextNormalization.Name(area.Name);
                    entry.Property("NormalizedName").CurrentValue = area.Name.ToLowerInvariant();
                    break;
                case DeviceCategory deviceCategory:
                    deviceCategory.DisplayCode = deviceCategory.DisplayCode.Trim();
                    entry.Property("NormalizedDisplayCode").CurrentValue = deviceCategory.DisplayCode.ToLowerInvariant();
                    deviceCategory.Name = TextNormalization.Name(deviceCategory.Name);
                    entry.Property("NormalizedName").CurrentValue = deviceCategory.Name.ToLowerInvariant();
                    break;
                case Supplier supplier:
                    supplier.DisplayCode = supplier.DisplayCode.Trim();
                    entry.Property("NormalizedDisplayCode").CurrentValue = supplier.DisplayCode.ToLowerInvariant();
                    supplier.Name = TextNormalization.Name(supplier.Name);
                    entry.Property("NormalizedName").CurrentValue = supplier.Name.ToLowerInvariant();
                    break;
                case Manufacturer manufacturer:
                    manufacturer.DisplayCode = manufacturer.DisplayCode.Trim();
                    entry.Property("NormalizedDisplayCode").CurrentValue = manufacturer.DisplayCode.ToLowerInvariant();
                    manufacturer.Name = TextNormalization.Name(manufacturer.Name);
                    entry.Property("NormalizedName").CurrentValue = manufacturer.Name.ToLowerInvariant();
                    break;
                case AuthLoginThrottle throttle when entry.State == EntityState.Added:
                    throttle.EmailKey = TextNormalization.Email(throttle.EmailKey);
                    break;
            }
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasSequence<long>("account_code_seq");
        modelBuilder.HasSequence<long>("department_code_seq");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CsvcDbContext).Assembly);
    }
}
