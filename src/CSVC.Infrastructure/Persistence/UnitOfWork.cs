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
    IAreaHistoryRepository areaHistories) : IUnitOfWork
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

    public Task<int> CommitAsync(CancellationToken cancellationToken = default)
        => context.SaveChangesAsync(cancellationToken);
}
