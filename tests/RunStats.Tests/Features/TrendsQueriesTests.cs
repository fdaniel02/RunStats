using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Options;
using RunStats.Data;
using RunStats.Features.Trends;
using RunStats.Features.Warnings;
using RunStats.Tests.Data;

namespace RunStats.Tests.Features;

[Collection("SqlServer")]
public sealed class TrendsQueriesTests(SqlServerFixture sqlServer) : IAsyncLifetime
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;
    static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    // Wednesday 30 September 2026, 12:00 in Amsterdam (summer time, UTC+2).
    readonly FixedTimeProvider time = new(new DateTimeOffset(2026, 9, 30, 10, 0, 0, TimeSpan.Zero));
    PooledDbContextFactory<AppDbContext> db = null!;
    TrendsQueries queries = null!;
    long nextId = 1;

    public async ValueTask InitializeAsync()
    {
        db = await MigratedDatabase.CreateAsync(sqlServer);
        var clock = new Clock(Amsterdam, time);
        var settings = Options.Create(new WarningSettings());
        queries = new TrendsQueries(db, new WarningQueries(db, settings, clock, time), settings, clock, time);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    static RunPaceBand Band(short pace, double minutes) => new() { PaceSecondsPerKm = pace, Seconds = (int)(minutes * 60), FlatSeconds = (int)(minutes * 60) };

    async Task AddAsync(DateTime startUtc, string typeKey, params RunPaceBand[] bands) => await AddAsync(startUtc, typeKey, null, bands);

    async Task AddAsync(DateTime startUtc, string typeKey, double? paceVariation, params RunPaceBand[] bands)
    {
        await using var context = await db.CreateDbContextAsync(Ct);
        context.Runs.Add(new Run
        {
            Id = nextId++,
            Name = "Run",
            TypeKey = typeKey,
            TypeId = 1,
            StartUtc = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc),
            StartLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(startUtc, DateTimeKind.Utc), Amsterdam),
            DurationSeconds = bands.Sum(b => b.Seconds),
            PaceVariationPercent = paceVariation,
            PaceBands = [.. bands]
        });
        await context.SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task Weeks_have_their_time_fast_and_very_fast_minutes()
    {
        // Sunday 27 September 23:30 in Amsterdam: still the week of the 21st.
        await AddAsync(new DateTime(2026, 9, 27, 21, 30, 0), "running", Band(335, 30), Band(300, 20), Band(240, 10));
        await AddAsync(new DateTime(2026, 9, 24, 15, 0, 0), "treadmill_running", Band(305, 15), Band(310, 5));
        await AddAsync(new DateTime(2026, 9, 29, 6, 0, 0), "running", Band(335, 40));

        var data = await queries.LoadAsync(Ct);

        Assert.Equal(TrendsQueries.WeekCount, data.Weeks.Count);
        var (previous, current) = (data.Weeks[^2], data.Weeks[^1]);
        Assert.Equal((new DateOnly(2026, 9, 21), 80 * 60.0, 45 * 60.0, 10 * 60.0, false),
            (previous.Monday, previous.Seconds, previous.FastSeconds, previous.VeryFastSeconds, previous.IsCurrent));
        Assert.Equal((new DateOnly(2026, 9, 28), 40 * 60.0, 0.0, true), (current.Monday, current.Seconds, current.FastSeconds, current.IsCurrent));
        Assert.Equal(new DateOnly(2026, 4, 6), data.Weeks[0].Monday);
        Assert.Equal((310, 255), (data.FastPaceSecondsPerKm, data.VeryFastPaceSecondsPerKm));
    }

    [Fact]
    public async Task Warnings_are_active_listed_as_episodes_and_marked_on_their_weeks()
    {
        await AddAsync(new DateTime(2026, 9, 24, 15, 0, 0), "running", Band(335, 20), Band(300, 45));
        await AddAsync(new DateTime(2026, 9, 27, 11, 0, 0), "running", Band(335, 20), Band(300, 30));

        var data = await queries.LoadAsync(Ct);

        Assert.Equal([WarningKind.FastJump, WarningKind.HardRunsBackToBack], data.Active.Select(w => w.Kind));
        Assert.Equal(
            [(WarningKind.HardRunsBackToBack, new DateOnly(2026, 9, 27)), (WarningKind.FastJump, new DateOnly(2026, 9, 24))],
            data.Episodes.Select(e => (e.Kind, e.From)));
        Assert.All(data.Episodes, e => Assert.Equal(new DateOnly(2026, 9, 30), e.To));
        Assert.Equal([WarningKind.FastJump, WarningKind.HardRunsBackToBack], data.Weeks[^2].Warnings);
        Assert.Equal([WarningKind.FastJump, WarningKind.HardRunsBackToBack], data.Weeks[^1].Warnings);
        Assert.Empty(data.Weeks[^3].Warnings);
    }

    [Fact]
    public async Task A_week_is_marked_only_for_the_warnings_its_own_running_set_off()
    {
        // Hard and fast on Thursday 10 and Sunday 13 September; nothing in the week after, while the warnings are still active.
        await AddAsync(new DateTime(2026, 9, 10, 15, 0, 0), "running", Band(335, 20), Band(300, 45));
        await AddAsync(new DateTime(2026, 9, 13, 8, 0, 0), "running", Band(335, 20), Band(300, 30));

        var data = await queries.LoadAsync(Ct);

        var weeks = data.Weeks.ToDictionary(w => w.Monday);
        Assert.Equal([WarningKind.FastJump, WarningKind.HardRunsBackToBack], weeks[new DateOnly(2026, 9, 7)].Warnings);
        Assert.Empty(weeks[new DateOnly(2026, 9, 14)].Warnings);
        // The episodes still show the days the warnings were active, into the quiet week.
        // The Sep 10 run leaves the 7 days during the 17th.
        Assert.All(data.Episodes, e => Assert.Equal(new DateOnly(2026, 9, 16), e.To));
    }

    [Fact]
    public async Task Typical_pace_is_the_median_of_the_outdoor_seconds_of_the_last_8_weeks()
    {
        await AddAsync(new DateTime(2026, 9, 20, 7, 0, 0), "running", Band(300, 10), Band(330, 25), Band(345, 20));
        // Left out: a treadmill run, and a run 9 weeks ago.
        await AddAsync(new DateTime(2026, 9, 22, 7, 0, 0), "treadmill_running", Band(360, 100));
        await AddAsync(new DateTime(2026, 7, 29, 7, 0, 0), "running", Band(400, 100));

        var data = await queries.LoadAsync(Ct);

        // 55 min: the 27.5th falls in the 5:30–5:35 band.
        Assert.Equal(332.5, data.TypicalPaceSecondsPerKm);
    }

    [Fact]
    public async Task Without_runs_everything_is_empty()
    {
        var data = await queries.LoadAsync(Ct);

        Assert.Empty(data.Active);
        Assert.Empty(data.Episodes);
        Assert.All(data.Weeks, w => Assert.Equal(0, w.Seconds));
        Assert.Null(data.TypicalPaceSecondsPerKm);
    }

    [Fact]
    public void Consecutive_days_of_a_warning_are_one_episode()
    {
        static DayWarnings Day(int day, params WarningKind[] kinds) =>
            new(new DateOnly(2026, 9, day), [.. kinds.Select(k => new Warning(k, $"{k} on {day}"))]);

        var episodes = TrendsQueries.Episodes(
            [Day(10, WarningKind.VolumeJump), Day(11, WarningKind.VolumeJump), Day(16, WarningKind.VeryFastJump, WarningKind.VolumeJump),
             Day(17, WarningKind.VeryFastJump)], new DateOnly(2026, 9, 30));

        Assert.Equal(
            [(WarningKind.VeryFastJump, 16, 17, "VeryFastJump on 16"), (WarningKind.VolumeJump, 16, 16, "VolumeJump on 16"),
             (WarningKind.VolumeJump, 10, 11, "VolumeJump on 10")],
            episodes.Select(e => (e.Kind, e.From.Day, e.To.Day, e.Message)));
    }

    [Fact]
    public async Task Pace_variation_is_the_median_of_each_months_outdoor_runs()
    {
        await AddAsync(new DateTime(2026, 7, 3, 7, 0, 0), "running", 3.0);
        await AddAsync(new DateTime(2026, 7, 10, 7, 0, 0), "running", 20.0);
        await AddAsync(new DateTime(2026, 7, 17, 7, 0, 0), "running", 2.5);
        await AddAsync(new DateTime(2026, 9, 3, 7, 0, 0), "running", 4.0);
        await AddAsync(new DateTime(2026, 9, 10, 7, 0, 0), "running", 5.0);
        // Left out: the treadmill, and runs without a value.
        await AddAsync(new DateTime(2026, 9, 11, 7, 0, 0), "treadmill_running", 1.0);
        await AddAsync(new DateTime(2026, 9, 12, 7, 0, 0), "running", (double?)null);

        var data = await queries.LoadAsync(Ct);

        Assert.Equal(
            [new PaceVariationMonth(new DateOnly(2026, 7, 1), 3.0, 3), new PaceVariationMonth(new DateOnly(2026, 8, 1), null, 0), new PaceVariationMonth(new DateOnly(2026, 9, 1), 4.5, 2)],
            data.PaceVariation);
    }

    [Fact]
    public async Task Without_outdoor_runs_there_is_no_pace_variation() => Assert.Empty((await queries.LoadAsync(Ct)).PaceVariation);
}
