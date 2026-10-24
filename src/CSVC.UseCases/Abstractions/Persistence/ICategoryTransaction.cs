namespace CSVC.UseCases.Abstractions.Persistence;

public interface ICategoryTransaction
{
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken);
}
