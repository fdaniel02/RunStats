using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RunStats.Data;
using RunStats.Garmin;

namespace RunStats.Processing;

// Fills a stored run's typed columns, laps, samples and track from its raw data.
public class RunProcessing(IDbContextFactory<AppDbContext> dbFactory, IGarminGateway garmin)
{
    const int MaxErrorLength = 1000;

    // Newest first, so recent runs are complete first when many are waiting.
    public async Task<List<long>> PendingAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Runs.Where(r => r.ProcessorVersion < RunProcessor.Version)
            .OrderByDescending(r => r.StartUtc).Select(r => r.Id).ToListAsync(cancellationToken);
    }

    // Returns why the run couldn't be processed, or null. Problems with the gateway itself are thrown: they aren't
    // the run's fault, and the next sync should try again.
    public async Task<string?> ProcessAsync(long runId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var run = await db.Runs.Include(r => r.RawData).SingleAsync(r => r.Id == runId, cancellationToken);
        List<RunLap> laps;
        List<RunSample> samples;
        try
        {
            var raw = run.RawData ?? throw new FormatException("The run has no raw data.");
            RunProcessor.ApplySummary(run, raw);
            laps = RunProcessor.Laps(runId, raw.SplitsJson);
            samples = raw.FitFile is null ? [] : RunProcessor.Samples(runId, await garmin.DecodeFitAsync(raw.FitFile, cancellationToken));
        }
        catch (Exception e) when (e is JsonException or FormatException or InvalidFitFileException)
        {
            var error = $"{e.GetType().Name}: {e.Message}";
            error = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
            // Without the half-applied summary: the run keeps what it had.
            db.ChangeTracker.Clear();
            await ReplaceAsync(db, runId, [], [], null, cancellationToken);
            await db.Runs.Where(r => r.Id == runId).ExecuteUpdateAsync(r => r
                .SetProperty(x => x.ProcessorVersion, RunProcessor.Version)
                .SetProperty(x => x.ProcessingError, error), cancellationToken);
            return error;
        }

        run.ProcessorVersion = RunProcessor.Version;
        run.ProcessingError = null;
        await ReplaceAsync(db, runId, laps, samples, TrackBuilder.Build(runId, samples), cancellationToken);
        return null;
    }

    // In one transaction, so a run never has the laps or samples of two processings.
    static Task ReplaceAsync(
        AppDbContext db, long runId, List<RunLap> laps, List<RunSample> samples, RunTrack? track, CancellationToken cancellationToken) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.RunLaps.Where(l => l.RunId == runId).ExecuteDeleteAsync(cancellationToken);
            await db.RunSamples.Where(s => s.RunId == runId).ExecuteDeleteAsync(cancellationToken);
            await db.RunTracks.Where(t => t.RunId == runId).ExecuteDeleteAsync(cancellationToken);
            db.RunLaps.AddRange(laps);
            db.RunSamples.AddRange(samples);
            if (track is not null)
                db.RunTracks.Add(track);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
}
