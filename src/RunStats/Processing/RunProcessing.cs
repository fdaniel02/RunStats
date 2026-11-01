using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RunStats.Data;
using RunStats.Garmin;

namespace RunStats.Processing;

// Fills a stored run's typed columns, laps, samples, pace bands, track and best efforts from its raw data.
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
            await ReplaceAsync(db, runId, RunParts.None, cancellationToken);
            await db.Runs.Where(r => r.Id == runId).ExecuteUpdateAsync(r => r
                .SetProperty(x => x.ProcessorVersion, RunProcessor.Version)
                .SetProperty(x => x.ProcessingError, error), cancellationToken);
            return error;
        }

        PaceVariation.Apply(run, laps, samples);
        run.ProcessorVersion = RunProcessor.Version;
        run.ProcessingError = null;
        var parts = new RunParts(laps, samples, PaceBandBuilder.Build(runId, samples), TrackBuilder.Build(runId, samples),
            BestEffortBuilder.Build(runId, run.TypeKey, samples), HeartRateBandBuilder.Build(runId, run.TypeKey, samples));
        await ReplaceAsync(db, runId, parts, cancellationToken);
        return null;
    }

    // In one transaction, so a run never has the laps or samples of two processings.
    static Task ReplaceAsync(AppDbContext db, long runId, RunParts parts, CancellationToken cancellationToken) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.RunLaps.Where(l => l.RunId == runId).ExecuteDeleteAsync(cancellationToken);
            await db.RunSamples.Where(s => s.RunId == runId).ExecuteDeleteAsync(cancellationToken);
            await db.RunPaceBands.Where(b => b.RunId == runId).ExecuteDeleteAsync(cancellationToken);
            await db.RunTracks.Where(t => t.RunId == runId).ExecuteDeleteAsync(cancellationToken);
            await db.RunBestEfforts.Where(e => e.RunId == runId).ExecuteDeleteAsync(cancellationToken);
            await db.RunHeartRateBands.Where(b => b.RunId == runId).ExecuteDeleteAsync(cancellationToken);
            db.RunLaps.AddRange(parts.Laps);
            db.RunSamples.AddRange(parts.Samples);
            db.RunPaceBands.AddRange(parts.PaceBands);
            db.RunBestEfforts.AddRange(parts.BestEfforts);
            db.RunHeartRateBands.AddRange(parts.HeartRateBands);
            if (parts.Track is not null)
                db.RunTracks.Add(parts.Track);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
}

// What processing builds from a run's raw data, besides its own columns.
sealed record RunParts(
    List<RunLap> Laps, List<RunSample> Samples, List<RunPaceBand> PaceBands, RunTrack? Track, List<RunBestEffort> BestEfforts,
    List<RunHeartRateBand> HeartRateBands)
{
    public static RunParts None => new([], [], [], null, [], []);
}
