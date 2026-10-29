namespace CSVC.UseCases.Abstractions.Persistence;

/// <summary>Serializes inventory writes with account authorization changes and locks device rows in a stable order.</summary>
public interface IInventoryTransaction
{
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default);
    Task LockDevicesAsync(IEnumerable<Guid> deviceIds, CancellationToken cancellationToken = default);
}
