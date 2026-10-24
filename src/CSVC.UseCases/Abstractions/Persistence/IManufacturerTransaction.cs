namespace CSVC.UseCases.Abstractions.Persistence;

public interface IManufacturerTransaction
{
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken);
}
