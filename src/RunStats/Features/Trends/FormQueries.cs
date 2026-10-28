using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RunStats.Data;
using RunStats.Features.Warnings;

namespace RunStats.Features.Trends;

// Ground contact and vertical oscillation (and so vertical ratio) read differently with the chest strap than with the
// watch alone, so they're compared per sensor; a run has strap data exactly when it has a balance.
public enum FormMetric
{
    StrideLength, Cadence, HeartRate,
    GroundContactWatch, GroundContactStrap, VerticalOscillationWatch, VerticalOscillationStrap, VerticalRatioWatch, VerticalRatioStrap
}

public enum Sensor { Any, Watch, Strap }

// Values are in Unit (Value converts from the band's unit); a change is in ChangeUnit, and also in percent where that
// means something (vertical ratio is a percentage already). MinChange is how far a chart's axis reaches at least on both
// sides of zero, so month-to-month noise doesn't fill it.
public sealed record FormMetricInfo(FormMetric Metric, string Title, string Unit, string ChangeUnit, string Format, Sensor Sensor,
    bool ShowPercent, double MinChange, Func<RunPaceBand, double?> Value);

// Null when there's too little running at comparable paces.
public sealed record FormTile(FormMetric Metric, Comparison? Comparison);

// Before is the long-term average at the same pace, Now the month's.
public sealed record FormMonth(DateOnly Month, Comparison? Comparison, double Seconds);

public sealed record FormSeries(FormMetric Metric, IReadOnlyList<FormMonth> Months);

public sealed record BalanceRun(long RunId, DateOnly Date, double LeftPercent);

public sealed record FormData(IReadOnlyList<FormTile> Tiles, IReadOnlyList<FormSeries> Series, IReadOnlyList<BalanceRun> Balance);

public interface IFormQueries
{
    Task<FormData> LoadAsync(CancellationToken cancellationToken);
}

public class FormQueries(IDbContextFactory<AppDbContext> dbFactory, IOptions<WarningSettings> settings, TimeProvider time) : IFormQueries
{
    // Fewer minutes at comparable paces make a month's point jump around.
    public const double MonthMinMinutes = 30;

    public static readonly IReadOnlyList<FormMetricInfo> Metrics =
    [
        // Garmin's "stride length" is the distance of one step, the FIT file's step_length.
        new(FormMetric.StrideLength, "Stride length", "cm", "cm", "0.0", Sensor.Any, true, 2, b => b.StepLengthMm / 10),
        new(FormMetric.Cadence, "Cadence", "spm", "spm", "0.0", Sensor.Any, true, 2, b => b.CadenceSpm),
        new(FormMetric.HeartRate, "Heart rate", "bpm", "bpm", "0", Sensor.Any, true, 3, b => b.HeartRate),
        new(FormMetric.GroundContactWatch, "Ground contact (watch)", "ms", "ms", "0", Sensor.Watch, true, 4, b => b.GroundContactTimeMs),
        new(FormMetric.GroundContactStrap, "Ground contact (strap)", "ms", "ms", "0", Sensor.Strap, true, 4, b => b.GroundContactTimeMs),
        new(FormMetric.VerticalOscillationWatch, "Vertical oscillation (watch)", "mm", "mm", "0.0", Sensor.Watch, true, 2, b => b.VerticalOscillationMm),
        new(FormMetric.VerticalOscillationStrap, "Vertical oscillation (strap)", "mm", "mm", "0.0", Sensor.Strap, true, 2, b => b.VerticalOscillationMm),
        new(FormMetric.VerticalRatioWatch, "Vertical ratio (watch)", "%", "pt", "0.0", Sensor.Watch, false, 0.4, b => b.VerticalRatioPercent),
        new(FormMetric.VerticalRatioStrap, "Vertical ratio (strap)", "%", "pt", "0.0", Sensor.Strap, false, 0.4, b => b.VerticalRatioPercent)
    ];

    // Strap data covers too few months for a line.
    public static readonly IReadOnlyList<FormMetric> MonthlyMetrics =
        [FormMetric.StrideLength, FormMetric.Cadence, FormMetric.HeartRate, FormMetric.GroundContactWatch, FormMetric.VerticalOscillationWatch, FormMetric.VerticalRatioWatch];

    public static FormMetricInfo Info(FormMetric metric) => Metrics.Single(m => m.Metric == metric);

    sealed record Run(DateTime StartUtc, DateTime StartLocal, long Id, List<RunPaceBand> PaceBands)
    {
        public bool HasStrap => PaceBands.Any(b => b.GroundContactBalanceLeftPercent is not null);
    }

    public async Task<FormData> LoadAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var runs = await db.Runs.AsNoTracking()
            .Where(r => r.TypeKey == "running")
            .OrderBy(r => r.StartUtc)
            .Select(r => new Run(r.StartUtc, r.StartLocal, r.Id, r.PaceBands.Where(b => b.FlatSeconds > 0).ToList()))
            .ToListAsync(cancellationToken);
        return new FormData(Tiles(runs), Series(runs), Balance(runs));
    }

    // The same periods as the form warning: the last 2 weeks against the 8 before. Strap tiles only with strap runs.
    List<FormTile> Tiles(List<Run> runs)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var recent = runs.Where(r => r.StartUtc > now.AddDays(-14) && r.StartUtc <= now).ToList();
        var baseline = runs.Where(r => r.StartUtc > now.AddDays(-70) && r.StartUtc <= now.AddDays(-14)).ToList();
        var hasStrap = recent.Concat(baseline).Any(r => r.HasStrap);
        return
        [
            .. Metrics.Where(m => m.Sensor != Sensor.Strap || hasStrap)
                .Select(m => new FormTile(m.Metric, Compare(m, Bands(recent, m.Sensor), Bands(baseline, m.Sensor))))
        ];
    }

    // Each month against the whole history at the same pace, from the first month with a run to the last.
    List<FormSeries> Series(List<Run> runs)
    {
        if (runs.Count == 0)
            return [.. MonthlyMetrics.Select(m => new FormSeries(m, []))];
        var byMonth = runs.ToLookup(r => new DateOnly(r.StartLocal.Year, r.StartLocal.Month, 1));
        var first = byMonth.Min(g => g.Key);
        var last = byMonth.Max(g => g.Key);
        var months = new List<DateOnly>();
        for (var month = first; month <= last; month = month.AddMonths(1))
            months.Add(month);
        return
        [
            .. MonthlyMetrics.Select(Info).Select(m =>
            {
                var all = Bands(runs, m.Sensor).ToList();
                return new FormSeries(m.Metric,
                [
                    .. months.Select(month =>
                    {
                        var comparison = Compare(m, Bands(byMonth[month], m.Sensor), all);
                        return comparison is { } c && c.Seconds >= MonthMinMinutes * 60
                            ? new FormMonth(month, c, c.Seconds)
                            : new FormMonth(month, null, comparison?.Seconds ?? 0);
                    })
                ]);
            })
        ];
    }

    static List<BalanceRun> Balance(List<Run> runs) =>
    [
        .. runs.Select(r => (Run: r, Mean: SamePace.Mean(r.PaceBands, b => b.GroundContactBalanceLeftPercent)))
            .Where(r => r.Mean is not null)
            .Select(r => new BalanceRun(r.Run.Id, DateOnly.FromDateTime(r.Run.StartLocal), r.Mean!.Value.Value))
    ];

    Comparison? Compare(FormMetricInfo metric, IEnumerable<RunPaceBand> recent, IEnumerable<RunPaceBand> baseline) =>
        WarningRules.Compare(recent, baseline, b => (float?)metric.Value(b), settings.Value);

    static IEnumerable<RunPaceBand> Bands(IEnumerable<Run> runs, Sensor sensor) => runs
        .Where(r => sensor == Sensor.Any || r.HasStrap == (sensor == Sensor.Strap))
        .SelectMany(r => r.PaceBands);
}
