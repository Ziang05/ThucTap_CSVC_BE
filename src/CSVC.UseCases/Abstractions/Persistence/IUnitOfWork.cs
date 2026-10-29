namespace CSVC.UseCases.Abstractions.Persistence;

public interface IUnitOfWork
{
    IRoleRepository Roles { get; }
    IDepartmentRepository Departments { get; }
    IDepartmentHistoryRepository DepartmentHistories { get; }
    IUserProfileRepository UserProfiles { get; }
    IAccountRepository Accounts { get; }
    IAccountHistoryRepository AccountHistories { get; }
    IAuthSessionRepository AuthSessions { get; }
    IAuthHistoryRepository AuthHistories { get; }
    IAuthLoginThrottleRepository AuthLoginThrottles { get; }
    IPasswordResetRequestRepository PasswordResetRequests { get; }
    IAreaRepository Areas { get; }
    IAreaHistoryRepository AreaHistories { get; }

    IDeviceCategoryRepository DeviceCategories { get; }
    ICategoryHistoryRepository CategoryHistories { get; }
    ISupplierRepository Suppliers { get; }
    ISupplierHistoryRepository SupplierHistories { get; }
    IManufacturerRepository Manufacturers { get; }
    IManufacturerHistoryRepository ManufacturerHistories { get; }

    IDeviceRepository Devices { get; }
    IDeviceIdentityRepository DeviceIdentities { get; }
    IDeviceHistoryRepository DeviceHistories { get; }
    IDeviceOperationHoldRepository DeviceOperationHolds { get; }
    IDeviceReceiptRepository DeviceReceipts { get; }
    IDeviceReceiptItemRepository DeviceReceiptItems { get; }
    IDeviceReceiptHistoryRepository DeviceReceiptHistories { get; }
    IDeviceLoanRepository DeviceLoans { get; }
    IDeviceLoanItemRepository DeviceLoanItems { get; }
    IDeviceLoanHistoryRepository DeviceLoanHistories { get; }
    IDeviceTransferRepository DeviceTransfers { get; }
    IDeviceTransferItemRepository DeviceTransferItems { get; }
    IDeviceTransferHistoryRepository DeviceTransferHistories { get; }

    Task<int> CommitAsync(CancellationToken cancellationToken = default);
}
