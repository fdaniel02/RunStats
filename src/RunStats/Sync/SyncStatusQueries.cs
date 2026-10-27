using Microsoft.EntityFrameworkCore;
using RunStats.Data;

namespace RunStats.Sync;

public sealed record SyncProgress(int? RunsTotal, int RunsDownloaded, int? RunsToProcess, int RunsProcessed);

public sealed record SyncFailure(DateTime FinishedAtUtc, string Error);

// LastFailure is only set when the latest finished sync failed; a later success clears it.
public sealed record SyncState(SyncProgress? Running, DateTime? LastSucceededAtUtc, SyncFailure? LastFailure);

public interface ISyncStatusQueries
{
    // In memory, so it can be checked often without the database.
    bool IsRunning { get; }

    Task<SyncState> LoadAsync(CancellationToken cancellationToken);

    Task<SyncState> StartAsync(CancellationToken cancellationToken);
}

public class SyncStatusQueries(SyncCoordinator coordinator, IDbContextFactory<AppDbContext> dbFactory) : ISyncStatusQueries
{
    public bool IsRunning => coordinator.RunningSyncId is not null;

    public async Task<SyncState> LoadAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        SyncProgress? running = null;
        if (coordinator.RunningSyncId is { } id)
            running = await db.SyncRuns.Where(s => s.Id == id && s.Status == SyncStatus.Running)
                .Select(s => new SyncProgress(s.RunsTotal, s.RunsDownloaded, s.RunsToProcess, s.RunsProcessed))
                .SingleOrDefaultAsync(cancellationToken);
        var lastSucceeded = await db.SyncRuns.Where(s => s.Status == SyncStatus.Succeeded).MaxAsync(s => s.FinishedAtUtc, cancellationToken);
        var lastFinished = await db.SyncRuns.Where(s => s.Status != SyncStatus.Running && s.FinishedAtUtc != null)
            .OrderByDescending(s => s.FinishedAtUtc).ThenByDescending(s => s.Id)
            .Select(s => new { s.Status, s.FinishedAtUtc, s.Error })
            .FirstOrDefaultAsync(cancellationToken);
        var failure = lastFinished is { Status: SyncStatus.Failed, FinishedAtUtc: { } failedAt }
            ? new SyncFailure(failedAt, lastFinished.Error ?? "Unknown error")
            : null;
        return new SyncState(running, lastSucceeded, failure);
    }

    public async Task<SyncState> StartAsync(CancellationToken cancellationToken)
    {
        await coordinator.RequestAsync(SyncTrigger.Manual, cancellationToken);
        return await LoadAsync(cancellationToken);
    }
}
