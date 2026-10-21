namespace CSVC.UseCases.Abstractions.Persistence;

// Serializes account writes (including last-manager checks) and saves data + audit atomically.
public interface IAccountTransaction
{
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken);
    Task LockDepartmentAsync(Guid departmentId, CancellationToken cancellationToken);
}
