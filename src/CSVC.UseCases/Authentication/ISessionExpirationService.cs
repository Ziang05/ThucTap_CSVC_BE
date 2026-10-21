namespace CSVC.UseCases.Authentication;

public interface ISessionExpirationService
{
    Task<int> ExpireAsync(int batchSize, CancellationToken cancellationToken = default);
    Task ExpireSessionAsync(Guid sessionId, Guid requestId, CancellationToken cancellationToken);
}
