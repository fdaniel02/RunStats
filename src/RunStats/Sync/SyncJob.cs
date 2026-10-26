using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using RunStats.Data;
using RunStats.Garmin;
using RunStats.Processing;

namespace RunStats.Sync;

// One sync: reads Garmin's whole activity list, downloads new and changed runs, removes runs deleted in Garmin,
// then processes the raw data of every run that needs it.
public class SyncJob(
    IDbContextFactory<AppDbContext> dbFactory,
    IGarminGateway garmin,
    RunProcessing processing,
    SyncMetrics metrics,
    TimeProvider time,
    ILogger<SyncJob> logger)
{
    public int PageSize { get; init; } = 100;
    const int MaxErrorLength = 2000;
    const int MaxNameLength = 200;

    public async Task RunAsync(int syncId, CancellationToken cancellationToken)
    {
        using var span = Telemetry.ActivitySource.StartActivity("Sync");
        var started = time.GetTimestamp();
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var sync = await db.SyncRuns.SingleAsync(s => s.Id == syncId, cancellationToken);
        span?.SetTag("runstats.sync.id", syncId);
        span?.SetTag("runstats.sync.trigger", sync.Trigger.ToString().ToLowerInvariant());
        logger.SyncStarted(syncId, sync.Trigger);
        try
        {
            var plan = await PlanAsync(db, cancellationToken);
            logger.SyncPlanned(syncId, plan.ToAdd.Count, plan.ToUpdate.Count, plan.ToDelete.Count, plan.Unchanged);
            sync.RunsTotal = plan.ToAdd.Count + plan.ToUpdate.Count;
            await db.SaveChangesAsync(cancellationToken);

            foreach (var activity in plan.Downloads)
            {
                if (await StoreAsync(activity, cancellationToken))
                    sync.RunsAdded++;
                else
                    sync.RunsUpdated++;
                sync.RunsDownloaded++;
                await db.SaveChangesAsync(cancellationToken);
            }

            // Only reached when the whole list was read, so a broken sync never deletes runs.
            if (plan.ToDelete.Count > 0)
                await db.Runs.Where(r => plan.ToDelete.Contains(r.Id)).ExecuteDeleteAsync(cancellationToken);
            sync.RunsDeleted = plan.ToDelete.Count;

            await ProcessAsync(db, sync, cancellationToken);

            await FinishAsync(db, sync, SyncStatus.Succeeded, null);
            metrics.LastSuccessAt(sync.FinishedAtUtc!.Value);
            metrics.RunsChanged("added", sync.RunsAdded);
            metrics.RunsChanged("updated", sync.RunsUpdated);
            metrics.RunsChanged("deleted", sync.RunsDeleted);
            metrics.RunsChanged("processed", sync.RunsProcessed);
            metrics.RunsChanged("processing_failed", sync.RunsFailedToProcess);
            var elapsed = time.GetElapsedTime(started);
            logger.SyncSucceeded(
                syncId, sync.RunsAdded, sync.RunsUpdated, sync.RunsDeleted, sync.RunsProcessed, sync.RunsFailedToProcess, elapsed.TotalSeconds);
        }
        catch (Exception e)
        {
            var error = e switch
            {
                OperationCanceledException when cancellationToken.IsCancellationRequested => "Interrupted: the app is stopping",
                GarminUnavailableException => e.Message,
                _ => $"{e.GetType().Name}: {e.Message}"
            };
            if (e is GarminUnavailableException or OperationCanceledException)
                logger.SyncStopped(syncId, error);
            else
                logger.SyncFailed(syncId, e);
            span?.SetStatus(ActivityStatusCode.Error, error);
            await FinishAsync(db, sync, SyncStatus.Failed, error);
        }
        span?.SetTag("runstats.sync.outcome", sync.Status.ToString().ToLowerInvariant());
        metrics.SyncFinished(sync.Trigger, sync.Status, time.GetElapsedTime(started));
    }

    async Task ProcessAsync(AppDbContext db, SyncRun sync, CancellationToken cancellationToken)
    {
        var pending = await processing.PendingAsync(cancellationToken);
        sync.RunsToProcess = pending.Count;
        await db.SaveChangesAsync(cancellationToken);
        foreach (var runId in pending)
        {
            if (await processing.ProcessAsync(runId, cancellationToken) is { } error)
            {
                sync.RunsFailedToProcess++;
                logger.RunProcessingFailed(runId, error);
            }
            else
            {
                sync.RunsProcessed++;
            }
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    async Task<SyncPlan> PlanAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var activities = new List<GarminActivity>();
        IReadOnlyList<GarminActivity> page;
        var start = 0;
        do
        {
            page = await garmin.GetActivitiesAsync(start, PageSize, cancellationToken);
            activities.AddRange(page);
            start += PageSize;
        } while (page.Count == PageSize);
        var stored = await db.Runs
            .Select(r => new { r.Id, ListJson = r.RawData != null ? r.RawData.ListJson : "" })
            .ToDictionaryAsync(r => r.Id, r => r.ListJson, cancellationToken);
        return SyncPlan.Create(activities, stored);
    }

    // Returns whether the run is new. The download happens before the database is touched, so a run is stored
    // completely or not at all.
    async Task<bool> StoreAsync(GarminActivity activity, CancellationToken cancellationToken)
    {
        var detail = await garmin.GetActivityAsync(activity.Id, cancellationToken);
        var splits = await garmin.GetSplitsAsync(activity.Id, cancellationToken);
        var heartRateZones = await garmin.GetHeartRateZonesAsync(activity.Id, cancellationToken);
        var weather = await garmin.GetWeatherAsync(activity.Id, cancellationToken);
        var fit = await garmin.GetFitFileAsync(activity.Id, cancellationToken);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var run = await db.Runs.Include(r => r.RawData).SingleOrDefaultAsync(r => r.Id == activity.Id, cancellationToken);
        var added = run is null;
        if (run is null)
        {
            run = new Run { Id = activity.Id };
            db.Runs.Add(run);
        }
        run.Name = activity.Name.Length > MaxNameLength ? activity.Name[..MaxNameLength] : activity.Name;
        run.TypeKey = activity.TypeKey;
        run.TypeId = activity.TypeId;
        run.ParentTypeId = activity.ParentTypeId;
        run.IsRace = activity.IsRace;
        run.StartUtc = activity.StartUtc;
        run.StartLocal = activity.StartLocal;
        run.DistanceMeters = activity.DistanceMeters;
        run.DurationSeconds = activity.DurationSeconds;
        run.ProcessorVersion = 0;
        run.RawData ??= new RunRawData();
        run.RawData.ListJson = activity.Json;
        run.RawData.DetailJson = detail;
        run.RawData.SplitsJson = splits;
        run.RawData.HeartRateZonesJson = heartRateZones;
        run.RawData.WeatherJson = weather;
        run.RawData.FitFile = fit;
        run.RawData.DownloadedAtUtc = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
        return added;
    }

    async Task FinishAsync(AppDbContext db, SyncRun sync, SyncStatus status, string? error)
    {
        sync.Status = status;
        sync.Error = error is { Length: > MaxErrorLength } ? error[..MaxErrorLength] : error;
        sync.FinishedAtUtc = time.GetUtcNow().UtcDateTime;
        // Not cancellable: when the app stops, the sync still has to be marked as interrupted.
        await db.SaveChangesAsync(CancellationToken.None);
    }
}
