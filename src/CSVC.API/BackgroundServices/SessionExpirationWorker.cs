using CSVC.UseCases.Authentication;
using Microsoft.Extensions.Options;

namespace CSVC.API.BackgroundServices;

public sealed class SessionExpirationWorker(
    IServiceScopeFactory scopes, IOptions<AuthenticationOptions> options, TimeProvider clock,
    ILogger<SessionExpirationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.ExpirationEnabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.ExpirationIntervalSeconds), clock);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    // A bounded pass avoids holding the account write lock across an unbounded backlog.
                    for (var batch = 0; batch < 10; batch++)
                    {
                        await using var scope = scopes.CreateAsyncScope();
                        var expiration = scope.ServiceProvider.GetRequiredService<ISessionExpirationService>();
                        if (await expiration.ExpireAsync(settings.ExpirationBatchSize, stoppingToken) < settings.ExpirationBatchSize) break;
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception exception)
                {
                    logger.LogError("Session expiration failed; retrying next interval. ErrorType: {ErrorType}", exception.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
