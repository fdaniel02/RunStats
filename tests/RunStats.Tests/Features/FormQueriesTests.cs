using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Options;
using RunStats.Data;
using RunStats.Features.Trends;
using RunStats.Features.Warnings;
using RunStats.Tests.Data;

namespace RunStats.Tests.Features;

[Collection("SqlServer")]
public sealed class FormQueriesTests(SqlServerFixture sqlServer) : IAsyncLifetime
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Wednesday 28 October 2026, 12:00 UTC.
    static readonly DateTime Now = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
    PooledDbContextFactory<AppDbContext> db = null!;
    FormQueries queries = null!;
    long nextId = 1;

    public async ValueTask InitializeAsync()
    {
        db = await MigratedDatabase.CreateAsync(sqlServer);
        queries = new FormQueries(db, Options.Create(new WarningSettings()), new FixedTimeProvider(Now));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    static RunPaceBand Band(double minutes, float step = 1020, float cadence = 174, float gct = 255, float? balance = null) => new()
    {
        PaceSecondsPerKm = 335,
        Seconds = (int)(minutes * 60),
        FlatSeconds = (int)(minutes * 60),
        StepLengthMm = step,
        CadenceSpm = cadence,
        HeartRate = 158,
        GroundContactTimeMs = gct,
        VerticalOscillationMm = 86,
        VerticalRatioPercent = 8.4f,
        GroundContactBalanceLeftPercent = balance
    };

    async Task<long> AddAsync(DateTime startUtc, RunPaceBand band, string typeKey = "running")
    {
        await using var context = await db.CreateDbContextAsync(Ct);
        var id = nextId++;
        context.Runs.Add(new Run
        {
            Id = id,
            Name = "Run",
            TypeKey = typeKey,
            TypeId = 1,
            StartUtc = startUtc,
            StartLocal = startUtc.AddHours(1),
            DurationSeconds = band.Seconds,
            PaceBands = [band]
        });
        await context.SaveChangesAsync(Ct);
        return id;
    }

    static FormTile Tile(FormData data, FormMetric metric) => data.Tiles.Single(t => t.Metric == metric);

    [Fact]
    public void Every_metric_has_its_info() =>
        Assert.All(Enum.GetValues<FormMetric>(), m => Assert.Equal(m, FormQueries.Info(m).Metric));

    [Fact]
    public async Task Tiles_compare_the_last_2_weeks_with_the_8_before()
    {
        for (var week = 3; week <= 9; week++)
            await AddAsync(Now.AddDays(-7 * week), Band(40));
        await AddAsync(Now.AddDays(-3), Band(40, step: 1071, cadence: 171));

        var data = await queries.LoadAsync(Ct);

        var stride = Tile(data, FormMetric.StrideLength).Comparison!;
        Assert.Equal((102.0, 107.1, 40 * 60.0), (Math.Round(stride.Before, 3), Math.Round(stride.Now, 3), stride.Seconds));
        Assert.Equal(5.0, stride.Percent, 3);
        Assert.Equal(-3.0, Tile(data, FormMetric.Cadence).Comparison!.Change, 3);
        Assert.Equal(0, Tile(data, FormMetric.HeartRate).Comparison!.Change);
    }

    [Fact]
    public async Task Without_recent_running_the_tiles_have_no_change()
    {
        await AddAsync(Now.AddDays(-30), Band(40));

        var data = await queries.LoadAsync(Ct);

        Assert.All(data.Tiles, t => Assert.Null(t.Comparison));
    }

    [Fact]
    public async Task Strap_tiles_need_strap_runs_and_compare_only_them()
    {
        // The strap measures ground contact shorter: watch and strap runs must not be compared with each other.
        for (var week = 3; week <= 6; week++)
            await AddAsync(Now.AddDays(-7 * week), Band(40, gct: 255));
        await AddAsync(Now.AddDays(-25), Band(40, gct: 228, balance: 50.3f));
        await AddAsync(Now.AddDays(-2), Band(40, gct: 232, balance: 50.5f));

        var data = await queries.LoadAsync(Ct);

        Assert.Equal((228, 232), (Tile(data, FormMetric.GroundContactStrap).Comparison!.Before, Tile(data, FormMetric.GroundContactStrap).Comparison!.Now));
        Assert.Null(Tile(data, FormMetric.GroundContactWatch).Comparison);
    }

    [Fact]
    public async Task Without_strap_runs_the_strap_tiles_are_left_out()
    {
        await AddAsync(Now.AddDays(-30), Band(40));
        await AddAsync(Now.AddDays(-3), Band(40));

        var data = await queries.LoadAsync(Ct);

        Assert.DoesNotContain(data.Tiles, t => FormQueries.Info(t.Metric).Sensor == Sensor.Strap);
        Assert.Contains(data.Tiles, t => t.Metric == FormMetric.GroundContactWatch);
    }

    [Fact]
    public async Task Months_compare_with_the_whole_history_and_thin_months_are_gaps()
    {
        await AddAsync(new DateTime(2026, 7, 10, 7, 0, 0, DateTimeKind.Utc), Band(60, step: 1000));
        await AddAsync(new DateTime(2026, 9, 10, 7, 0, 0, DateTimeKind.Utc), Band(60, step: 1040));
        // 20 min: below the 30 a month needs.
        await AddAsync(new DateTime(2026, 10, 5, 7, 0, 0, DateTimeKind.Utc), Band(20, step: 1100));
        await AddAsync(new DateTime(2026, 10, 6, 7, 0, 0, DateTimeKind.Utc), Band(60, step: 2000), typeKey: "treadmill_running");

        var data = await queries.LoadAsync(Ct);

        var step = data.Series.Single(s => s.Metric == FormMetric.StrideLength).Months;
        Assert.Equal([new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1)], step.Select(m => m.Month));
        // The history, thin October included and the treadmill left out, averages 144 400 / 140 mm; in cm.
        const double history = 14_440 / 140.0;
        Assert.Equal((history, 100), (step[0].Comparison!.Before, step[0].Comparison!.Now), new Close());
        Assert.Null(step[1].Comparison);
        Assert.Equal((history, 104), (step[2].Comparison!.Before, step[2].Comparison!.Now), new Close());
        Assert.Null(step[3].Comparison);
        Assert.Equal(20 * 60, step[3].Seconds);
        Assert.Equal(FormQueries.MonthlyMetrics, data.Series.Select(s => s.Metric));
    }

    [Fact]
    public async Task Balance_has_one_point_per_strap_run_in_date_order()
    {
        var second = await AddAsync(new DateTime(2026, 9, 22, 7, 0, 0, DateTimeKind.Utc), Band(40, balance: 50.3f));
        var first = await AddAsync(new DateTime(2026, 7, 11, 7, 0, 0, DateTimeKind.Utc), Band(40, balance: 49.7f));
        await AddAsync(new DateTime(2026, 9, 24, 7, 0, 0, DateTimeKind.Utc), Band(40));

        var data = await queries.LoadAsync(Ct);

        Assert.Equal([(first, new DateOnly(2026, 7, 11)), (second, new DateOnly(2026, 9, 22))], data.Balance.Select(b => (b.RunId, b.Date)));
        Assert.Equal([49.7, 50.3], data.Balance.Select(b => Math.Round(b.LeftPercent, 1)));
    }

    [Fact]
    public async Task Without_runs_everything_is_empty()
    {
        var data = await queries.LoadAsync(Ct);

        Assert.All(data.Tiles, t => Assert.Null(t.Comparison));
        Assert.All(data.Series, s => Assert.Empty(s.Months));
        Assert.Empty(data.Balance);
    }

    sealed class Close : IEqualityComparer<(double, double)>
    {
        public bool Equals((double, double) x, (double, double) y) => Math.Abs(x.Item1 - y.Item1) < 1e-3 && Math.Abs(x.Item2 - y.Item2) < 1e-3;
        public int GetHashCode((double, double) obj) => 0;
    }
}
