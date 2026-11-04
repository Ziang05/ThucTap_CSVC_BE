namespace CSVC.UseCases.Abstractions.Persistence;

public interface IDamageReportTransaction
{
    bool CanDiscardFiles { get; }
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct);
}
