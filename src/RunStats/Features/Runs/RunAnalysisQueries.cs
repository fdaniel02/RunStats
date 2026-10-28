using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RunStats.Data;
using RunStats.Features.Trends;
using RunStats.Features.Warnings;
using RunStats.Processing;

namespace RunStats.Features.Runs;

// One pace band of the run (or all of them, PaceSecondsPerKm null) against the same pace before; a metric is null where
// the other side has too little time at that pace.
public sealed record BandComparison(int? PaceSecondsPerKm, double Seconds, IReadOnlyDictionary<FormMetric, Comparison?> Metrics);

// The run against the runs of the same type in the weeks before it; ground contact and vertical oscillation only
// against runs with the same sensor.
public sealed record UsualForm(int BaselineRuns, bool HasStrap, BandComparison Overall, IReadOnlyList<BandComparison> Bands);

public enum DriftMissing { TooShort, NotSteady }

// The last third of the run against its first third, at the same pace.
public sealed record FormDrift(IReadOnlyDictionary<FormMetric, Comparison?> Metrics, DriftMissing? Missing);

public sealed record RunAnalysis(UsualForm Usual, FormDrift Drift, IReadOnlyList<Warning> Triggered);

public interface IRunAnalysisQueries
{
    Task<RunAnalysis?> LoadAsync(long runId, CancellationToken cancellationToken);
}

public class RunAnalysisQueries(IDbContextFactory<AppDbContext> dbFactory, IWarningQueries warnings, IOptions<WarningSettings> settings)
    : IRunAnalysisQueries
{
    public const int BaselineDays = 56;
    // A third of a shorter run is too little time at one pace to compare.
    public const double DriftMinMinutes = 30;

    public static IReadOnlyList<FormMetric> UsualMetrics(bool strap) => strap
        ? [FormMetric.StrideLength, FormMetric.Cadence, FormMetric.GroundContactStrap, FormMetric.VerticalOscillationStrap]
        : [FormMetric.StrideLength, FormMetric.Cadence, FormMetric.GroundContactWatch, FormMetric.VerticalOscillationWatch];

    public static IReadOnlyList<FormMetric> DriftMetrics(bool strap) =>
        [FormMetric.StrideLength, FormMetric.Cadence, strap ? FormMetric.GroundContactStrap : FormMetric.GroundContactWatch, FormMetric.HeartRate];

    public async Task<RunAnalysis?> LoadAsync(long runId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var run = await db.Runs.AsNoTracking().Where(r => r.Id == runId)
            .Select(r => new { r.StartUtc, r.TypeKey, r.DurationSeconds, r.ElapsedDurationSeconds, r.MovingDurationSeconds, Bands = r.PaceBands.ToList() })
            .SingleOrDefaultAsync(cancellationToken);
        if (run is null)
            return null;
        var fromUtc = run.StartUtc.AddDays(-BaselineDays);
        var baseline = await db.Runs.AsNoTracking()
            .Where(r => r.TypeKey == run.TypeKey && r.StartUtc >= fromUtc && r.StartUtc < run.StartUtc)
            .Select(r => r.PaceBands.ToList())
            .ToListAsync(cancellationToken);
        var samples = await db.RunSamples.AsNoTracking().Where(s => s.RunId == runId).OrderBy(s => s.OffsetSeconds).ToListAsync(cancellationToken);

        var usual = Usual(run.Bands, baseline);
        var drift = Drift(runId, samples, run.MovingDurationSeconds ?? run.DurationSeconds, HasStrap(run.Bands));
        // What the rules said just before the run and right after it: the warnings only after are this run's.
        var endUtc = run.StartUtc.AddSeconds(run.ElapsedDurationSeconds ?? run.DurationSeconds);
        var startAndEnd = await warnings.AtAsync([run.StartUtc.AddSeconds(-1), endUtc], cancellationToken);
        var triggered = startAndEnd[1].Where(w => startAndEnd[0].All(b => b.Kind != w.Kind)).ToList();
        return new RunAnalysis(usual, drift, triggered);
    }

    static bool HasStrap(IEnumerable<RunPaceBand> bands) => bands.Any(b => b.GroundContactBalanceLeftPercent is not null);

    UsualForm Usual(List<RunPaceBand> run, List<List<RunPaceBand>> baselineRuns)
    {
        var hasStrap = HasStrap(run);
        var all = baselineRuns.SelectMany(b => b).ToList();
        // The strap reads ground contact shorter than the watch alone.
        var sameSensor = baselineRuns.Where(b => HasStrap(b) == hasStrap).SelectMany(b => b).ToList();
        var width = settings.Value.FormBandWidthSecondsPerKm;
        var minSeconds = settings.Value.FormBandMinMinutes * 60;
        var flat = run.Where(b => b.FlatSeconds > 0).ToList();
        BandComparison Compare(int? pace, List<RunPaceBand> bands) => new(pace, bands.Sum(b => b.FlatSeconds),
            UsualMetrics(hasStrap).ToDictionary(m => m, m => WarningRules.Compare(bands, FormQueries.Info(m).Sensor == Sensor.Any ? all : sameSensor,
                b => (float?)FormQueries.Info(m).Value(b), settings.Value)));
        var bands = flat.GroupBy(b => b.PaceSecondsPerKm / width * width)
            .Where(g => g.Sum(b => b.FlatSeconds) >= minSeconds)
            .OrderBy(g => g.Key)
            .Select(g => Compare(g.Key, [.. g]))
            .ToList();
        return new UsualForm(baselineRuns.Count, hasStrap, Compare(null, flat), bands);
    }

    FormDrift Drift(long runId, List<RunSample> samples, double movingSeconds, bool strap)
    {
        var empty = DriftMetrics(strap).ToDictionary(m => m, _ => (Comparison?)null);
        if (movingSeconds < DriftMinMinutes * 60 || samples.Count == 0)
            return new FormDrift(empty, DriftMissing.TooShort);
        var third = (samples[^1].OffsetSeconds - samples[0].OffsetSeconds) / 3.0;
        var first = PaceBandBuilder.Build(runId, samples.Where(s => s.OffsetSeconds < samples[0].OffsetSeconds + third));
        var last = PaceBandBuilder.Build(runId, samples.Where(s => s.OffsetSeconds >= samples[^1].OffsetSeconds - third));
        var metrics = DriftMetrics(strap).ToDictionary(m => m, m => WarningRules.Compare(last, first, b => (float?)FormQueries.Info(m).Value(b), settings.Value));
        // Without enough time at the same pace in both thirds (intervals, a hilly loop) there's nothing to compare.
        return metrics.Values.All(c => c is null) ? new FormDrift(empty, DriftMissing.NotSteady) : new FormDrift(metrics, null);
    }
}
