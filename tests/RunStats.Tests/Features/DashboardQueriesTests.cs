using Microsoft.EntityFrameworkCore.Infrastructure;
using RunStats.Data;
using RunStats.Features.Dashboard;
using RunStats.Tests.Data;

namespace RunStats.Tests.Features;

[Collection("SqlServer")]
public sealed class DashboardQueriesTests(SqlServerFixture sqlServer) : IAsyncLifetime
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;
    static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    // Tuesday 27 October 2026, 10:00 in Amsterdam (winter time, UTC+1).
    readonly FixedTimeProvider time = new(new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero));
    PooledDbContextFactory<AppDbContext> db = null!;
    DashboardQueries queries = null!;
    long nextId = 1;

    public async ValueTask InitializeAsync()
    {
        db = await MigratedDatabase.CreateAsync(sqlServer);
        queries = new DashboardQueries(db, new Clock(Amsterdam, time));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    async Task AddAsync(DateTime startUtc, double km, double minutes, string typeKey = "running", bool race = false, string name = "Run")
    {
        await using var context = await db.CreateDbContextAsync(Ct);
        context.Runs.Add(new Run
        {
            Id = nextId++,
            Name = name,
            TypeKey = typeKey,
            TypeId = 1,
            ParentTypeId = 17,
            IsRace = race,
            StartUtc = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc),
            StartLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(startUtc, DateTimeKind.Utc), Amsterdam),
            DistanceMeters = km * 1000,
            DurationSeconds = minutes * 60
        });
        await context.SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task Year_and_month_totals_follow_the_apps_time_zone()
    {
        await AddAsync(new DateTime(2025, 12, 31, 22, 30, 0), 5, 30);   // 31 Dec 23:30 in Amsterdam: last year
        await AddAsync(new DateTime(2025, 12, 31, 23, 30, 0), 6, 33);   // 1 Jan 00:30 in Amsterdam: this year
        await AddAsync(new DateTime(2026, 9, 30, 21, 59, 0), 10, 50);   // 30 Sep 23:59 (summer time): September
        await AddAsync(new DateTime(2026, 9, 30, 22, 0, 0), 8, 40);     // 1 Oct 00:00: October
        await AddAsync(new DateTime(2026, 10, 26, 17, 0, 0), 7.5, 40);

        var data = await queries.LoadAsync(null, Ct);

        Assert.Equal(new Totals(4, 31500, 163 * 60), data.Year);
        Assert.Equal(new Totals(2, 15500, 80 * 60), data.Month);
    }

    [Fact]
    public async Task Without_runs_everything_is_empty()
    {
        var data = await queries.LoadAsync(null, Ct);

        Assert.Equal(new Totals(0, 0, 0), data.Year);
        Assert.Equal(new Totals(0, 0, 0), data.Month);
        Assert.Empty(data.RaceYears);
        Assert.Null(data.RaceYear);
        Assert.Empty(data.Races);
    }

    [Fact]
    public async Task Weeks_are_the_last_twelve_from_monday_including_empty_ones()
    {
        await AddAsync(new DateTime(2026, 8, 9, 8, 0, 0), 20, 100);     // Sunday before the first week: left out
        await AddAsync(new DateTime(2026, 8, 10, 6, 0, 0), 10, 50);     // Monday 10 Aug 08:00 local: first week
        // Summer time ended on 25 Oct, so these are 23:30 on Sunday and 00:30 on Monday in Amsterdam.
        await AddAsync(new DateTime(2026, 10, 25, 22, 30, 0), 5, 30);
        await AddAsync(new DateTime(2026, 10, 25, 23, 30, 0), 6, 33);
        await AddAsync(new DateTime(2026, 10, 27, 7, 0, 0), 4, 25);

        var weeks = await queries.WeeksAsync(Ct);

        Assert.Equal(DashboardQueries.WeekCount, weeks.Count);
        Assert.Equal(new WeekTotal(new DateOnly(2026, 8, 10), 10000, 3000, false), weeks[0]);
        Assert.All(weeks[1..10], w => Assert.Equal((0.0, 0.0), (w.DistanceMeters, w.DurationSeconds)));
        Assert.Equal(new WeekTotal(new DateOnly(2026, 10, 19), 5000, 1800, false), weeks[10]);
        Assert.Equal(new WeekTotal(new DateOnly(2026, 10, 26), 10000, 58 * 60, true), weeks[11]);
    }

    [Fact]
    public async Task Last_runs_are_the_ten_newest_with_their_local_date()
    {
        for (var day = 1; day <= 12; day++)
            await AddAsync(new DateTime(2026, 10, day, 22, 30, 0), day, 30, day % 2 == 0 ? "treadmill_running" : "running");

        var runs = await queries.LastRunsAsync(Ct);

        Assert.Equal(DashboardQueries.LastRunCount, runs.Count);
        Assert.Equal(new DateOnly(2026, 10, 13), runs[0].Date);
        Assert.Equal(new DateOnly(2026, 10, 4), runs[^1].Date);
        Assert.Equal(("treadmill_running", 12000.0, 1800.0), (runs[0].TypeKey, runs[0].DistanceMeters, runs[0].DurationSeconds));
    }

    async Task AddRacesAsync()
    {
        await AddAsync(new DateTime(2024, 4, 14, 8, 0, 0), 10, 50, race: true, name: "Zevenheuvelenloop");
        await AddAsync(new DateTime(2025, 10, 12, 8, 0, 0), 21.1, 120, race: true, name: "Marathon Eindhoven");
        // 31 Dec 2025 23:30 UTC is New Year's Day in Amsterdam: a 2026 race by its own date.
        await AddAsync(new DateTime(2025, 12, 31, 23, 30, 0), 5, 25, race: true, name: "New Year's run");
        await AddAsync(new DateTime(2026, 5, 31, 8, 0, 0), 15.5, 90, race: true, name: "Vestingloop");
        await AddAsync(new DateTime(2026, 6, 1, 8, 0, 0), 5, 30);
    }

    [Fact]
    public async Task Race_years_are_the_years_with_races_newest_first()
    {
        await AddRacesAsync();

        Assert.Equal([2026, 2025, 2024], await queries.RaceYearsAsync(Ct));
    }

    [Fact]
    public async Task Races_of_a_year_are_the_runs_marked_as_race_newest_first()
    {
        await AddRacesAsync();

        Assert.Equal(["Vestingloop", "New Year's run"], (await queries.RacesAsync(2026, Ct)).Select(r => r.Name));
        Assert.Equal(["Marathon Eindhoven"], (await queries.RacesAsync(2025, Ct)).Select(r => r.Name));
        Assert.Empty(await queries.RacesAsync(2023, Ct));
    }

    [Theory]
    [InlineData(null, 2026)]
    [InlineData(2024, 2024)]
    [InlineData(2019, 2026)]
    public async Task Races_show_the_requested_year_or_else_this_year(int? requested, int shown)
    {
        await AddRacesAsync();

        var data = await queries.LoadAsync(requested, Ct);

        Assert.Equal([2026, 2025, 2024], data.RaceYears);
        Assert.Equal(shown, data.RaceYear);
        Assert.All(data.Races, r => Assert.Equal(shown, r.Date.Year));
    }

    [Fact]
    public async Task Without_races_this_year_the_latest_year_with_races_is_shown()
    {
        await AddAsync(new DateTime(2024, 4, 14, 8, 0, 0), 10, 50, race: true);
        await AddAsync(new DateTime(2025, 10, 12, 8, 0, 0), 21.1, 120, race: true);

        var data = await queries.LoadAsync(null, Ct);

        Assert.Equal(2025, data.RaceYear);
        Assert.Single(data.Races);
    }

    [Fact]
    public async Task Records_are_the_known_running_types_in_order()
    {
        await using (var context = await db.CreateDbContextAsync(Ct))
        {
            PersonalRecord Record(int type, double value) => new()
            {
                TypeId = type, Value = value, RunId = 1, RawJson = "{}",
                AchievedAtUtc = new DateTime(2026, 9, 27, 8, 0, 0, DateTimeKind.Utc), AchievedAtLocal = new DateTime(2026, 9, 27, 10, 0, 0),
                UpdatedAtUtc = DateTime.UtcNow
            };
            context.PersonalRecords.AddRange(Record(7, 21530), Record(3, 1429), Record(99, 1));
            await context.SaveChangesAsync(Ct);
        }

        var records = await queries.RecordsAsync(Ct);

        Assert.Equal([new RecordRow(3, 1429, new DateOnly(2026, 9, 27), 1), new RecordRow(7, 21530, new DateOnly(2026, 9, 27), 1)], records);
    }

    [Theory]
    [InlineData("2026-10-26", "2026-10-26")]
    [InlineData("2026-10-27", "2026-10-26")]
    [InlineData("2026-11-01", "2026-10-26")]
    [InlineData("2026-11-02", "2026-11-02")]
    public void Weeks_start_on_monday(string date, string monday) =>
        Assert.Equal(DateOnly.Parse(monday), DashboardQueries.Monday(DateOnly.Parse(date)));

    [Fact]
    public async Task Run_days_are_the_dates_with_a_run_in_the_grids_weeks()
    {
        // The grid's 16 weeks start on Monday 2026-07-13 (today is Tuesday 2026-10-27).
        await AddAsync(new DateTime(2026, 7, 12, 8, 0, 0), 5, 30);     // Sunday before the grid
        await AddAsync(new DateTime(2026, 7, 13, 8, 0, 0), 5, 30);
        await AddAsync(new DateTime(2026, 10, 17, 13, 0, 0), 7.5, 40);
        await AddAsync(new DateTime(2026, 10, 17, 17, 0, 0), 3, 20);   // a second run the same day
        await AddAsync(new DateTime(2026, 10, 26, 22, 30, 0), 6, 35);  // 23:30 in Amsterdam: still the 26th

        var data = await queries.LoadAsync(null, Ct);

        Assert.Equal([new DateOnly(2026, 7, 13), new DateOnly(2026, 10, 17), new DateOnly(2026, 10, 26)], data.RunDays);
    }
}
