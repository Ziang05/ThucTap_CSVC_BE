namespace CSVC.UseCases.Abstractions.Persistence;

public interface ISupplierTransaction
{
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken);
}
