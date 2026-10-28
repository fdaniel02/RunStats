using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Options;
using RunStats.Data;
using RunStats.Features.Runs;
using RunStats.Features.Trends;
using RunStats.Features.Warnings;
using RunStats.Tests.Data;

namespace RunStats.Tests.Features;

[Collection("SqlServer")]
public sealed class RunAnalysisQueriesTests(SqlServerFixture sqlServer) : IAsyncLifetime
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;
    static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
    static readonly DateTime RunStart = new(2026, 9, 24, 15, 0, 0, DateTimeKind.Utc);

    PooledDbContextFactory<AppDbContext> db = null!;
    RunAnalysisQueries queries = null!;
    long nextId = 1;

    public async ValueTask InitializeAsync()
    {
        db = await MigratedDatabase.CreateAsync(sqlServer);
        var settings = Options.Create(new WarningSettings());
        var time = new FixedTimeProvider(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));
        queries = new RunAnalysisQueries(db, new WarningQueries(db, settings, new Clock(Amsterdam, time), time), settings);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    static RunPaceBand Band(short pace, double minutes, float stride = 1020, float cadence = 174, float gct = 255, float? balance = null) => new()
    {
        PaceSecondsPerKm = pace,
        Seconds = (int)(minutes * 60),
        FlatSeconds = (int)(minutes * 60),
        StepLengthMm = stride,
        CadenceSpm = cadence,
        HeartRate = 158,
        GroundContactTimeMs = gct,
        VerticalOscillationMm = 86,
        GroundContactBalanceLeftPercent = balance
    };

    async Task<long> AddAsync(DateTime startUtc, IEnumerable<RunPaceBand> bands, string typeKey = "running", List<RunSample>? samples = null)
    {
        await using var context = await db.CreateDbContextAsync(Ct);
        var id = nextId++;
        var list = bands.ToList();
        context.Runs.Add(new Run
        {
            Id = id,
            Name = "Run",
            TypeKey = typeKey,
            TypeId = 1,
            StartUtc = startUtc,
            StartLocal = TimeZoneInfo.ConvertTimeFromUtc(startUtc, Amsterdam),
            DurationSeconds = list.Sum(b => b.Seconds),
            PaceBands = list,
            Samples = samples ?? []
        });
        await context.SaveChangesAsync(Ct);
        return id;
    }

    async Task AddBaselineAsync(string typeKey = "running", float gct = 255, float? balance = null)
    {
        for (var week = 1; week <= 7; week++)
            await AddAsync(RunStart.AddDays(-7 * week), [Band(335, 30, gct: gct, balance: balance)], typeKey);
    }

    [Fact]
    public async Task The_run_is_compared_per_pace_with_the_8_weeks_before_it()
    {
        await AddBaselineAsync();
        // Older than 8 weeks, after the run, or another type: left out.
        await AddAsync(RunStart.AddDays(-60), [Band(335, 60, stride: 2000)]);
        await AddAsync(RunStart.AddDays(3), [Band(335, 60, stride: 2000)]);
        await AddAsync(RunStart.AddDays(-2), [Band(335, 60, stride: 2000)], "treadmill_running");
        var id = await AddAsync(RunStart, [Band(335, 20, stride: 1071, cadence: 170), Band(290, 29), Band(300, 3)]);

        var usual = (await queries.LoadAsync(id, Ct))!.Usual;

        Assert.Equal(7, usual.BaselineRuns);
        var stride = usual.Overall.Metrics[FormMetric.StrideLength]!;
        Assert.Equal((102.0, 107.1), (Math.Round(stride.Before, 3), Math.Round(stride.Now, 3)));
        Assert.Equal(-4, usual.Overall.Metrics[FormMetric.Cadence]!.Change, 3);
        // 5:00 has 3 min, under the 5 a row needs; 4:45 is a pace the weeks before didn't have.
        Assert.Equal([285, 330], usual.Bands.Select(b => b.PaceSecondsPerKm!.Value));
        Assert.All(usual.Bands[0].Metrics.Values, Assert.Null);
        Assert.Equal(29 * 60, usual.Bands[0].Seconds);
        Assert.NotNull(usual.Bands[1].Metrics[FormMetric.StrideLength]);
    }

    [Fact]
    public async Task Ground_contact_is_compared_only_with_runs_of_the_same_sensor()
    {
        await AddBaselineAsync(gct: 255);
        await AddBaselineAsync(gct: 228, balance: 50.2f);
        var id = await AddAsync(RunStart, [Band(335, 30, gct: 232, balance: 50.5f)]);

        var usual = (await queries.LoadAsync(id, Ct))!.Usual;

        Assert.True(usual.HasStrap);
        Assert.Equal(RunAnalysisQueries.UsualMetrics(true), usual.Overall.Metrics.Keys);
        Assert.Equal((228, 232), (usual.Overall.Metrics[FormMetric.GroundContactStrap]!.Before, usual.Overall.Metrics[FormMetric.GroundContactStrap]!.Now));
    }

    [Fact]
    public async Task Without_runs_before_there_is_nothing_to_compare()
    {
        var id = await AddAsync(RunStart, [Band(335, 30)]);

        var usual = (await queries.LoadAsync(id, Ct))!.Usual;

        Assert.Equal(0, usual.BaselineRuns);
        Assert.All(usual.Overall.Metrics.Values, Assert.Null);
    }

    // One sample a second at a steady 5:33/km; the cadence changes over the run as given.
    static List<RunSample> Steady(int minutes, Func<int, float> cadence) =>
    [
        .. Enumerable.Range(0, minutes * 60).Select(i => new RunSample
        {
            OffsetSeconds = i, SpeedMps = 3, DistanceMeters = i * 3, AltitudeMeters = 5, CadenceSpm = cadence(i), StepLengthMm = 1080, HeartRate = 150,
            GroundContactTimeMs = 250
        })
    ];

    [Fact]
    public async Task Drift_compares_the_last_third_with_the_first()
    {
        // 45 min, the cadence dropping from 176 to 170 in the last third.
        var id = await AddAsync(RunStart, [Band(330, 45)], samples: Steady(45, i => i >= 30 * 60 ? 170 : 176));

        var drift = (await queries.LoadAsync(id, Ct))!.Drift;

        Assert.Null(drift.Missing);
        Assert.Equal((176, 170), (drift.Metrics[FormMetric.Cadence]!.Before, drift.Metrics[FormMetric.Cadence]!.Now));
        Assert.Equal(0, drift.Metrics[FormMetric.StrideLength]!.Change, 3);
        Assert.Equal(RunAnalysisQueries.DriftMetrics(false), drift.Metrics.Keys);
    }

    [Fact]
    public async Task Drift_needs_a_long_enough_run()
    {
        var id = await AddAsync(RunStart, [Band(330, 25)], samples: Steady(25, _ => 174));

        Assert.Equal(DriftMissing.TooShort, (await queries.LoadAsync(id, Ct))!.Drift.Missing);
    }

    [Fact]
    public async Task Drift_needs_the_same_pace_in_both_thirds()
    {
        // Easy at the start, fast at the end: no pace both thirds share.
        var samples = Steady(45, _ => 174);
        foreach (var s in samples.Where(s => s.OffsetSeconds >= 30 * 60))
            s.SpeedMps = 4.2f;

        var id = await AddAsync(RunStart, [Band(330, 45)], samples: samples);

        Assert.Equal(DriftMissing.NotSteady, (await queries.LoadAsync(id, Ct))!.Drift.Missing);
    }

    [Fact]
    public async Task Warnings_set_off_by_the_run_are_the_ones_after_it_and_not_before()
    {
        // Hard on the 21st; the run on the 24th is the second hard run within 72 h and adds fast minutes.
        for (var week = 1; week <= 4; week++)
            await AddAsync(RunStart.AddDays(-7 * week), [Band(335, 45), Band(300, 5)]);
        await AddAsync(RunStart.AddDays(-3), [Band(335, 20), Band(300, 25)]);
        var id = await AddAsync(RunStart, [Band(335, 20), Band(300, 40)]);

        var triggered = (await queries.LoadAsync(id, Ct))!.Triggered;

        Assert.Equal([WarningKind.FastJump, WarningKind.HardRunsBackToBack], triggered.Select(w => w.Kind));
    }

    [Fact]
    public async Task A_warning_already_active_before_the_run_is_not_its_own()
    {
        for (var week = 1; week <= 4; week++)
            await AddAsync(RunStart.AddDays(-7 * week), [Band(335, 45)]);
        await AddAsync(RunStart.AddDays(-2), [Band(335, 10), Band(300, 50)]);
        var id = await AddAsync(RunStart, [Band(335, 30), Band(300, 10)]);

        Assert.Empty((await queries.LoadAsync(id, Ct))!.Triggered);
    }

    [Fact]
    public async Task An_unknown_run_has_no_analysis() => Assert.Null(await queries.LoadAsync(999, Ct));
}
