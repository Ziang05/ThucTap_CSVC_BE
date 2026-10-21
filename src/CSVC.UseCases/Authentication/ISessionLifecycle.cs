using CSVC.Core.Authentication;

namespace CSVC.UseCases.Authentication;

public interface ISessionLifecycle
{
    // Caller must hold IAccountTransaction; this method stages the state change and audit together.
    Task EndAsync(AuthSession session, Guid requestId, Guid? actorId, DateTimeOffset now, CancellationToken cancellationToken);
}
