using CSVC.Core.Authentication;
using CSVC.Core.Common;
using CSVC.UseCases.Abstractions.Persistence;

namespace CSVC.UseCases.Authentication;

public sealed class SessionLifecycle(IUnitOfWork unitOfWork) : ISessionLifecycle
{
    public async Task EndAsync(AuthSession session, Guid requestId, Guid? actorId, DateTimeOffset now, CancellationToken cancellationToken, string? reasonCode = null)
    {
        if (!session.IsActive) return;
        session.IsActive = false;
        unitOfWork.AuthSessions.Update(session);
        if (await unitOfWork.AuthHistories.ExistsAsync(
                history => history.SessionId == session.Id && history.ActionCode == "SESSION_ENDED", cancellationToken)) return;
        await unitOfWork.AuthHistories.AddAsync(new AuthHistory
        {
            RequestId = requestId, ActorAccountId = session.ExpiresAt <= now ? null : actorId,
            AccountId = session.AccountId, SessionId = session.Id,
            ActionCode = "SESSION_ENDED", Outcome = AuditOutcome.Success,
            ReasonCode = session.ExpiresAt <= now ? null : reasonCode,
            OccurredAt = session.ExpiresAt <= now ? session.ExpiresAt : now
        }, cancellationToken);
    }
}
