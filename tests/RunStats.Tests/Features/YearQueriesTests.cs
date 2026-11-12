using Microsoft.EntityFrameworkCore.Infrastructure;
using RunStats.Data;
using RunStats.Features.Dashboard;
using RunStats.Features.Years;
using RunStats.Tests.Data;

namespace RunStats.Tests.Features;

[Collection("Postgres")]
public sealed class YearQueriesTests(PostgresFixture postgres) : IAsyncLifetime
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;
    static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    // Tuesday 27 October 2026, 10:00 in Amsterdam.
    readonly FixedTimeProvider time = new(new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero));
    PooledDbContextFactory<AppDbContext> db = null!;
    YearQueries queries = null!;
    long nextId = 1;

    public async ValueTask InitializeAsync()
    {
        db = await MigratedDatabase.CreateAsync(postgres);
        queries = new YearQueries(db, new Clock(Amsterdam, time));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // The start is Garmin's local time; UTC is an hour earlier, as in Amsterdam's winter.
    async Task<long> AddAsync(DateTime startLocal, double km, double minutes, bool race = false, string name = "Run",
        string? trainingEffect = null, double? elapsedMinutes = null, params (int TypeId, double Seconds)[] efforts)
    {
        var id = nextId++;
        await using var context = await db.CreateDbContextAsync(Ct);
        context.Runs.Add(new Run
        {
            Id = id,
            Name = name,
            TypeKey = "running",
            TypeId = 1,
            ParentTypeId = 17,
            IsRace = race,
            StartLocal = startLocal,
            StartUtc = DateTime.SpecifyKind(startLocal.AddHours(-1), DateTimeKind.Utc),
            DistanceMeters = km * 1000,
            DurationSeconds = minutes * 60,
            ElapsedDurationSeconds = elapsedMinutes * 60,
            TrainingEffectLabel = trainingEffect,
            BestEfforts = [.. efforts.Select(e => new RunBestEffort { TypeId = e.TypeId, Seconds = e.Seconds })]
        });
        await context.SaveChangesAsync(Ct);
        return id;
    }

    [Fact]
    public async Task Years_are_those_with_runs_and_this_year_newest_first()
    {
        await AddAsync(new DateTime(2024, 4, 14, 10, 0, 0), 10, 50);
        await AddAsync(new DateTime(2022, 6, 1, 10, 0, 0), 5, 30);

        var data = await queries.LoadAsync(2024, Ct);

        Assert.Equal([2026, 2024, 2022], data!.Years);
    }

    [Fact]
    public async Task A_year_without_runs_is_not_shown_except_this_year()
    {
        await AddAsync(new DateTime(2024, 4, 14, 10, 0, 0), 10, 50);

        Assert.Null(await queries.LoadAsync(2023, Ct));
        var thisYear = await queries.LoadAsync(2026, Ct);
        Assert.Equal(new Totals(0, 0, 0), thisYear!.Totals);
        Assert.Empty(thisYear.Runs);
        Assert.Empty(thisYear.Records);
        Assert.Equal(12, thisYear.Months.Count);
    }

    [Fact]
    public async Task Totals_and_runs_go_by_the_runs_own_date_newest_first()
    {
        await AddAsync(new DateTime(2025, 12, 31, 23, 30, 0), 5, 30, name: "Old year");
        await AddAsync(new DateTime(2026, 1, 1, 0, 30, 0), 6, 33, name: "New Year");
        await AddAsync(new DateTime(2026, 10, 26, 18, 0, 0), 7.5, 40, name: "Latest");
        await AddAsync(new DateTime(2027, 1, 1, 0, 0, 0), 3, 20, name: "Next year");

        var data = await queries.LoadAsync(2026, Ct);

        Assert.Equal(2026, data!.Year);
        Assert.Equal(new Totals(2, 13500, 73 * 60), data.Totals);
        Assert.Equal(["Latest", "New Year"], data.Runs.Select(r => r.Name));
    }

    [Fact]
    public async Task Weeks_are_the_grids_columns_counting_only_the_years_days()
    {
        // 2026's grid starts on Monday 29 December 2025.
        await AddAsync(new DateTime(2025, 12, 30, 10, 0, 0), 20, 100);
        await AddAsync(new DateTime(2026, 1, 2, 10, 0, 0), 10, 50);
        await AddAsync(new DateTime(2026, 10, 27, 7, 0, 0), 4, 25);
        await AddAsync(new DateTime(2026, 11, 1, 20, 0, 0), 6, 35);

        var weeks = (await queries.LoadAsync(2026, Ct))!.Weeks;

        Assert.Equal(53, weeks.Count);
        Assert.Equal(new WeekTotal(new DateOnly(2025, 12, 29), 10000, 3000, false), weeks[0]);
        Assert.Equal(new WeekTotal(new DateOnly(2026, 10, 26), 10000, 60 * 60, true), weeks[43]);
        Assert.Equal(new DateOnly(2026, 12, 28), weeks[^1].Monday);
        Assert.Single(weeks, w => w.IsCurrent);
    }

    [Fact]
    public async Task Months_are_all_twelve_and_this_one_is_marked()
    {
        await AddAsync(new DateTime(2026, 1, 31, 23, 0, 0), 10, 50);
        await AddAsync(new DateTime(2026, 2, 1, 7, 0, 0), 5, 30);
        await AddAsync(new DateTime(2026, 10, 3, 7, 0, 0), 8, 40);

        var months = (await queries.LoadAsync(2026, Ct))!.Months;

        Assert.Equal(Enumerable.Range(1, 12), months.Select(m => m.Month));
        Assert.Equal(new MonthTotal(1, 10000, 3000, false), months[0]);
        Assert.Equal(new MonthTotal(2, 5000, 1800, false), months[1]);
        Assert.Equal(new MonthTotal(10, 8000, 2400, true), months[9]);
        Assert.Equal((0.0, 0.0), (months[11].DistanceMeters, months[11].DurationSeconds));
    }

    [Fact]
    public async Task A_past_year_has_no_current_week_or_month()
    {
        await AddAsync(new DateTime(2025, 10, 28, 7, 0, 0), 8, 40);

        var data = await queries.LoadAsync(2025, Ct);

        Assert.DoesNotContain(data!.Weeks, w => w.IsCurrent);
        Assert.DoesNotContain(data.Months, m => m.IsCurrent);
    }

    [Fact]
    public async Task Run_days_are_shaded_by_the_hardest_run_of_the_day()
    {
        await AddAsync(new DateTime(2026, 3, 1, 8, 0, 0), 5, 30, trainingEffect: "RECOVERY");
        await AddAsync(new DateTime(2026, 3, 1, 18, 0, 0), 5, 25, trainingEffect: "TEMPO");
        await AddAsync(new DateTime(2026, 3, 4, 8, 0, 0), 5, 30);

        var days = (await queries.LoadAsync(2026, Ct))!.RunDays;

        Assert.Equal([new RunDay(new DateOnly(2026, 3, 1), "TEMPO"), new RunDay(new DateOnly(2026, 3, 4), null)], days);
    }

    [Fact]
    public async Task Records_are_the_fastest_efforts_of_the_years_runs_and_its_longest_run()
    {
        // Faster last year: that 5 km doesn't count for 2026.
        await AddAsync(new DateTime(2025, 6, 1, 9, 0, 0), 5, 22, efforts: [(1, 250), (3, 1300)]);
        var first = await AddAsync(new DateTime(2026, 3, 1, 9, 0, 0), 10, 52, efforts: [(1, 290), (3, 1500), (4, 3100)]);
        var faster = await AddAsync(new DateTime(2026, 5, 1, 9, 0, 0), 5, 25, efforts: [(1, 280), (3, 1480)]);
        var tie = await AddAsync(new DateTime(2026, 6, 1, 9, 0, 0), 5, 26, efforts: [(1, 280)]);
        var longest = await AddAsync(new DateTime(2026, 9, 1, 9, 0, 0), 21.1, 120);

        var records = (await queries.LoadAsync(2026, Ct))!.Records;

        Assert.Equal(
        [
            new RecordRow(1, 280, new DateOnly(2026, 5, 1), faster),
            new RecordRow(3, 1480, new DateOnly(2026, 5, 1), faster),
            new RecordRow(4, 3100, new DateOnly(2026, 3, 1), first),
            new RecordRow(7, 21100, new DateOnly(2026, 9, 1), longest)
        ], records);
        Assert.DoesNotContain(records, r => r.RunId == tie);
    }

    [Fact]
    public async Task Distance_counts_cover_only_the_years_runs_rounded_to_the_km()
    {
        await AddAsync(new DateTime(2025, 6, 1, 9, 0, 0), 9, 50);
        await AddAsync(new DateTime(2026, 3, 1, 9, 0, 0), 2.4, 15);
        await AddAsync(new DateTime(2026, 3, 2, 9, 0, 0), 2.5, 15);
        await AddAsync(new DateTime(2026, 3, 3, 9, 0, 0), 3.2, 20);

        var counts = (await queries.LoadAsync(2026, Ct))!.DistanceCounts;

        Assert.Equal([new DistanceCount(1, 0), new DistanceCount(2, 1), new DistanceCount(3, 2)], counts);
    }

    [Fact]
    public async Task Run_times_are_the_years_in_order_by_elapsed_time_or_else_duration()
    {
        await AddAsync(new DateTime(2025, 6, 1, 9, 0, 0), 5, 30);
        await AddAsync(new DateTime(2026, 5, 1, 18, 0, 0), 5, 30);
        await AddAsync(new DateTime(2026, 3, 1, 9, 0, 0), 5, 30, elapsedMinutes: 35);

        var times = (await queries.LoadAsync(2026, Ct))!.RunTimes;

        Assert.Equal(
        [
            new RunTime(new DateTime(2026, 3, 1, 9, 0, 0), 2100),
            new RunTime(new DateTime(2026, 5, 1, 18, 0, 0), 1800)
        ], times);
    }

    [Fact]
    public async Task The_goal_is_the_shown_years()
    {
        await AddAsync(new DateTime(2025, 4, 14, 10, 0, 0), 10, 50);
        await using (var context = await db.CreateDbContextAsync(Ct))
        {
            context.YearGoals.Add(new YearGoal { Year = 2026, DistanceMeters = 1_500_000 });
            await context.SaveChangesAsync(Ct);
        }

        Assert.Equal(1_500_000, (await queries.LoadAsync(2026, Ct))!.GoalMeters);
        Assert.Null((await queries.LoadAsync(2025, Ct))!.GoalMeters);
    }

    [Fact]
    public async Task Races_are_the_years_runs_marked_as_race()
    {
        await AddAsync(new DateTime(2025, 10, 12, 9, 0, 0), 21.1, 120, race: true, name: "Last year");
        await AddAsync(new DateTime(2026, 5, 31, 9, 0, 0), 15.5, 90, race: true, name: "Vestingloop");
        await AddAsync(new DateTime(2026, 6, 1, 9, 0, 0), 5, 30);
        await AddAsync(new DateTime(2026, 10, 17, 9, 0, 0), 7.5, 40, race: true, name: "Amsterdam");

        var races = (await queries.LoadAsync(2026, Ct))!.Races;

        Assert.Equal(["Amsterdam", "Vestingloop"], races.Select(r => r.Name));
    }
}
