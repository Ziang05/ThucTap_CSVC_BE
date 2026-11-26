using CSVC.UseCases.Maintenance;

namespace CSVC.API.BackgroundServices;

public sealed class MaintenanceScheduleWorker(IServiceScopeFactory scopes, IConfiguration configuration,
    ILogger<MaintenanceScheduleWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Maintenance:SchedulesEnabled", true)) return;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<MaintenanceScheduleService>().AdvanceAllAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Maintenance schedule processing failed; the next scheduled run will retry.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
