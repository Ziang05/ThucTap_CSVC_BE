using CSVC.UseCases.Abstractions.Persistence;

namespace CSVC.UseCases.Authentication;

public sealed class SessionExpirationService(
    IUnitOfWork unitOfWork, IAccountTransaction transaction, ISessionLifecycle lifecycle, TimeProvider clock) : ISessionExpirationService
{
    public Task<int> ExpireAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(batchSize, 1000);
        return transaction.ExecuteAsync(async ct =>
        {
            var now = clock.GetUtcNow();
            var sessions = await unitOfWork.AuthSessions.GetExpiredAsync(now, batchSize, ct);
            var requestId = Guid.NewGuid();
            foreach (var session in sessions) await lifecycle.EndAsync(session, requestId, null, now, ct);
            return sessions.Count;
        }, cancellationToken);
    }

    public async Task ExpireSessionAsync(Guid sessionId, Guid requestId, CancellationToken cancellationToken)
        => await transaction.ExecuteAsync(async ct =>
        {
            var now = clock.GetUtcNow();
            var session = await unitOfWork.AuthSessions.FirstOrDefaultAsync(
                item => item.Id == sessionId && item.IsActive && item.ExpiresAt <= now, ct);
            if (session is not null) await lifecycle.EndAsync(session, requestId, null, now, ct);
            return true;
        }, cancellationToken);
}
