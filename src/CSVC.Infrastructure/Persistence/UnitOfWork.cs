using CSVC.UseCases.Abstractions.Persistence;

namespace CSVC.Infrastructure.Persistence;

public sealed class UnitOfWork(
    CsvcDbContext context,
    IRoleRepository roles,
    IDepartmentRepository departments,
    IDepartmentHistoryRepository departmentHistories,
    IUserProfileRepository userProfiles,
    IAccountRepository accounts,
    IAccountHistoryRepository accountHistories,
    IAuthSessionRepository authSessions,
    IAuthHistoryRepository authHistories,
    IAuthLoginThrottleRepository authLoginThrottles,
    IPasswordResetRequestRepository passwordResetRequests,
    IAreaRepository areas,
    IAreaHistoryRepository areaHistories,
    IDeviceCategoryRepository deviceCategories,
    ICategoryHistoryRepository categoryHistories,
    ISupplierRepository suppliers,
    ISupplierHistoryRepository supplierHistories,
    IManufacturerRepository manufacturers,
    IManufacturerHistoryRepository manufacturerHistories,
    IDeviceRepository devices,
    IDeviceIdentityRepository deviceIdentities,
    IDeviceHistoryRepository deviceHistories,
    IDeviceOperationHoldRepository deviceOperationHolds,
    IDeviceReceiptRepository deviceReceipts,
    IDeviceReceiptItemRepository deviceReceiptItems,
    IDeviceReceiptHistoryRepository deviceReceiptHistories,
    IDeviceLoanRepository deviceLoans,
    IDeviceLoanItemRepository deviceLoanItems,
    IDeviceLoanHistoryRepository deviceLoanHistories,
    IDeviceTransferRepository deviceTransfers,
    IDeviceTransferItemRepository deviceTransferItems,
    IDeviceTransferHistoryRepository deviceTransferHistories) : IUnitOfWork
{
    public IRoleRepository Roles { get; } = roles;

    public IDepartmentRepository Departments { get; } = departments;

    public IDepartmentHistoryRepository DepartmentHistories { get; } = departmentHistories;

    public IUserProfileRepository UserProfiles { get; } = userProfiles;

    public IAccountRepository Accounts { get; } = accounts;

    public IAccountHistoryRepository AccountHistories { get; } = accountHistories;

    public IAuthSessionRepository AuthSessions { get; } = authSessions;

    public IAuthHistoryRepository AuthHistories { get; } = authHistories;

    public IAuthLoginThrottleRepository AuthLoginThrottles { get; } = authLoginThrottles;

    public IPasswordResetRequestRepository PasswordResetRequests { get; } = passwordResetRequests;

    public IAreaRepository Areas { get; } = areas;

    public IAreaHistoryRepository AreaHistories { get; } = areaHistories;

    public IDeviceCategoryRepository DeviceCategories { get; } = deviceCategories;

    public ICategoryHistoryRepository CategoryHistories { get; } = categoryHistories;

    public ISupplierRepository Suppliers { get; } = suppliers;

    public ISupplierHistoryRepository SupplierHistories { get; } = supplierHistories;

    public IManufacturerRepository Manufacturers { get; } = manufacturers;

    public IManufacturerHistoryRepository ManufacturerHistories { get; } = manufacturerHistories;

    public IDeviceRepository Devices { get; } = devices;
    public IDeviceIdentityRepository DeviceIdentities { get; } = deviceIdentities;
    public IDeviceHistoryRepository DeviceHistories { get; } = deviceHistories;
    public IDeviceOperationHoldRepository DeviceOperationHolds { get; } = deviceOperationHolds;
    public IDeviceReceiptRepository DeviceReceipts { get; } = deviceReceipts;
    public IDeviceReceiptItemRepository DeviceReceiptItems { get; } = deviceReceiptItems;
    public IDeviceReceiptHistoryRepository DeviceReceiptHistories { get; } = deviceReceiptHistories;
    public IDeviceLoanRepository DeviceLoans { get; } = deviceLoans;
    public IDeviceLoanItemRepository DeviceLoanItems { get; } = deviceLoanItems;
    public IDeviceLoanHistoryRepository DeviceLoanHistories { get; } = deviceLoanHistories;
    public IDeviceTransferRepository DeviceTransfers { get; } = deviceTransfers;
    public IDeviceTransferItemRepository DeviceTransferItems { get; } = deviceTransferItems;
    public IDeviceTransferHistoryRepository DeviceTransferHistories { get; } = deviceTransferHistories;

    public Task<int> CommitAsync(CancellationToken cancellationToken = default)
        => context.SaveChangesAsync(cancellationToken);
}
