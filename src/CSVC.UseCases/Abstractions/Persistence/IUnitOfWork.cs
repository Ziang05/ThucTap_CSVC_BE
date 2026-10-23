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

    Task<int> CommitAsync(CancellationToken cancellationToken = default);
}
