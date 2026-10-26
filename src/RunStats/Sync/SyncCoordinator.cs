using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using RunStats.Data;

namespace RunStats.Sync;

public sealed record SyncRequest(int SyncId, bool Started);

// Starts syncs one at a time; both the API (CronJob) and the UI come through here.
public sealed class SyncCoordinator(
    IDbContextFactory<AppDbContext> dbFactory, SyncMetrics metrics, TimeProvider time, ILogger<SyncCoordinator> logger) : IDisposable
{
    readonly Channel<int> queue = Channel.CreateUnbounded<int>(new UnboundedChannelOptions { SingleReader = true });
    readonly SemaphoreSlim gate = new(1, 1);
    int? running;

    public ChannelReader<int> Queue => queue.Reader;

    public int? RunningSyncId => running;

    // At startup, before requests come in: a sync that was running when the app stopped will never finish.
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var now = time.GetUtcNow().UtcDateTime;
        var interrupted = await db.SyncRuns.Where(s => s.Status == SyncStatus.Running).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.Status, SyncStatus.Failed)
            .SetProperty(x => x.FinishedAtUtc, now)
            .SetProperty(x => x.Error, "Interrupted: the app stopped during the sync"), cancellationToken);
        if (interrupted > 0)
            logger.SyncsInterrupted(interrupted);
        var lastSuccess = await db.SyncRuns.Where(s => s.Status == SyncStatus.Succeeded).MaxAsync(s => s.FinishedAtUtc, cancellationToken);
        if (lastSuccess is { } finishedAt)
            metrics.LastSuccessAt(finishedAt);
    }

    public async Task<SyncRequest> RequestAsync(SyncTrigger trigger, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (running is { } runningId)
                return new SyncRequest(runningId, Started: false);
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var sync = new SyncRun { Trigger = trigger, Status = SyncStatus.Running, StartedAtUtc = time.GetUtcNow().UtcDateTime };
            db.SyncRuns.Add(sync);
            await db.SaveChangesAsync(cancellationToken);
            running = sync.Id;
            queue.Writer.TryWrite(sync.Id);
            return new SyncRequest(sync.Id, Started: true);
        }
        finally
        {
            gate.Release();
        }
    }

    public void Finished(int syncId)
    {
        gate.Wait();
        try
        {
            if (running == syncId)
                running = null;
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose() => gate.Dispose();
}
