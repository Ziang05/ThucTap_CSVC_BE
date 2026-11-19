using CSVC.UseCases.Inventories;

namespace CSVC.API.BackgroundServices;

public sealed class InventoryReminderWorker(IServiceScopeFactory scopes, IConfiguration configuration,
    ILogger<InventoryReminderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Inventory:RemindersEnabled", true)) return;
        // Wait until startup completes; API startup never requires the inventory migration to exist.
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IInventoryService>().RemindAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception) { logger.LogError(exception, "Inventory reminders failed; the next scheduled run will retry."); }
        }
    }
}
