using CSVC.Core.Accounts;
using CSVC.Core.Areas;
using CSVC.Core.Authentication;
using CSVC.Core.Common;
using CSVC.Core.Departments;
using CSVC.Core.DeviceCategories;
using CSVC.Core.Suppliers;
using CSVC.Core.Manufacturers;
using CSVC.Core.Devices;
using CSVC.Core.DeviceReceipts;
using CSVC.Core.DeviceLoans;
using CSVC.Core.DeviceTransfers;
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

    public DbSet<Device> Devices => Set<Device>();
    public DbSet<DeviceIdentity> DeviceIdentities => Set<DeviceIdentity>();
    public DbSet<DeviceHistory> DeviceHistories => Set<DeviceHistory>();
    public DbSet<DeviceOperationHold> DeviceOperationHolds => Set<DeviceOperationHold>();
    public DbSet<DeviceReceipt> DeviceReceipts => Set<DeviceReceipt>();
    public DbSet<DeviceReceiptItem> DeviceReceiptItems => Set<DeviceReceiptItem>();
    public DbSet<DeviceReceiptHistory> DeviceReceiptHistories => Set<DeviceReceiptHistory>();
    public DbSet<DeviceLoan> DeviceLoans => Set<DeviceLoan>();
    public DbSet<DeviceLoanItem> DeviceLoanItems => Set<DeviceLoanItem>();
    public DbSet<DeviceLoanHistory> DeviceLoanHistories => Set<DeviceLoanHistory>();
    public DbSet<DeviceTransfer> DeviceTransfers => Set<DeviceTransfer>();
    public DbSet<DeviceTransferItem> DeviceTransferItems => Set<DeviceTransferItem>();
    public DbSet<DeviceTransferHistory> DeviceTransferHistories => Set<DeviceTransferHistory>();

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
                    entry.Property("NormalizedTaxCode").CurrentValue = string.IsNullOrWhiteSpace(supplier.TaxCode)
                        ? null : supplier.TaxCode.Trim().ToUpperInvariant();
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
                case Device device:
                    device.DeviceCode = device.DeviceCode.Trim().ToUpperInvariant();
                    device.Barcode = device.Barcode.Trim().ToUpperInvariant();
                    device.Name = TextNormalization.Name(device.Name);
                    if (entry.State == EntityState.Modified)
                        device.Version = (int)entry.Property(nameof(Device.Version)).OriginalValue! + 1;
                    break;
                case DeviceIdentity identity:
                    identity.DeviceCode = identity.DeviceCode.Trim().ToUpperInvariant();
                    identity.Barcode = identity.Barcode.Trim().ToUpperInvariant();
                    break;
                case DeviceReceipt receipt:
                    receipt.ReceiptCode = receipt.ReceiptCode.Trim().ToUpperInvariant();
                    receipt.DeliveryBatch = TextNormalization.Name(receipt.DeliveryBatch);
                    receipt.CarrierName = string.IsNullOrWhiteSpace(receipt.CarrierName) ? null : TextNormalization.Name(receipt.CarrierName);
                    receipt.RequestCode = string.IsNullOrWhiteSpace(receipt.RequestCode) ? null : receipt.RequestCode.Trim();
                    receipt.PaidInvoiceCode = string.IsNullOrWhiteSpace(receipt.PaidInvoiceCode) ? null : receipt.PaidInvoiceCode.Trim();
                    if (entry.State == EntityState.Modified)
                        receipt.Version = (int)entry.Property(nameof(DeviceReceipt.Version)).OriginalValue! + 1;
                    break;
                case DeviceReceiptItem receiptItem:
                    receiptItem.DeviceCode = receiptItem.DeviceCode.Trim().ToUpperInvariant();
                    receiptItem.Barcode = receiptItem.Barcode.Trim().ToUpperInvariant();
                    receiptItem.Name = TextNormalization.Name(receiptItem.Name);
                    break;
                case DeviceLoan loan:
                    loan.LoanCode = loan.LoanCode.Trim().ToUpperInvariant();
                    loan.Purpose = TextNormalization.Name(loan.Purpose);
                    if (entry.State == EntityState.Modified)
                        loan.Version = (int)entry.Property(nameof(DeviceLoan.Version)).OriginalValue! + 1;
                    break;
                case DeviceTransfer transfer:
                    transfer.TransferCode = transfer.TransferCode.Trim().ToUpperInvariant();
                    transfer.Reason = TextNormalization.Name(transfer.Reason);
                    if (entry.State == EntityState.Modified)
                        transfer.Version = (int)entry.Property(nameof(DeviceTransfer.Version)).OriginalValue! + 1;
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
