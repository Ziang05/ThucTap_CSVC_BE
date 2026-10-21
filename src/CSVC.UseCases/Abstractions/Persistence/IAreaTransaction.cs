namespace CSVC.UseCases.Abstractions.Persistence;

public interface IAreaTransaction
{
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken);
}
