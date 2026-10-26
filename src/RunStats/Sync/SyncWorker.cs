namespace RunStats.Sync;

public class SyncWorker(SyncCoordinator coordinator, IServiceScopeFactory scopes, ILogger<SyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var syncId in coordinator.Queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                // A scope per sync: the job's HttpClient comes from the factory and shouldn't live for the whole app.
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<SyncJob>().RunAsync(syncId, stoppingToken);
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            {
                // The job records its own failures; this is for when even that failed, e.g. the database is down.
                logger.SyncCrashed(syncId, e);
            }
            finally
            {
                coordinator.Finished(syncId);
            }
        }
    }
}
