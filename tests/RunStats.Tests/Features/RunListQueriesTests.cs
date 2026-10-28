using Microsoft.EntityFrameworkCore.Infrastructure;
using RunStats.Data;
using RunStats.Features.Runs;
using RunStats.Shared;
using RunStats.Tests.Data;

namespace RunStats.Tests.Features;

[Collection("SqlServer")]
public sealed class RunListQueriesTests(SqlServerFixture sqlServer) : IAsyncLifetime
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    PooledDbContextFactory<AppDbContext> db = null!;
    RunListQueries queries = null!;
    long nextId = 1;

    public async ValueTask InitializeAsync()
    {
        db = await MigratedDatabase.CreateAsync(sqlServer);
        queries = new RunListQueries(db);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    async Task AddAsync(DateTime startLocal, string typeKey = "running", bool race = false, string name = "Run", double km = 10, double minutes = 50)
    {
        await using var context = await db.CreateDbContextAsync(Ct);
        context.Runs.Add(NewRun(startLocal, typeKey, race, name, km, minutes));
        await context.SaveChangesAsync(Ct);
    }

    Run NewRun(DateTime startLocal, string typeKey = "running", bool race = false, string name = "Run", double km = 10, double minutes = 50) => new()
    {
        Id = nextId++,
        Name = name,
        TypeKey = typeKey,
        TypeId = 1,
        IsRace = race,
        StartLocal = startLocal,
        StartUtc = DateTime.SpecifyKind(startLocal.AddHours(-1), DateTimeKind.Utc),
        DistanceMeters = km * 1000,
        DurationSeconds = minutes * 60
    };

    async Task AddRunsAsync()
    {
        await AddAsync(new DateTime(2025, 10, 12, 9, 0, 0), race: true, name: "Eindhoven", km: 21.1, minutes: 115);
        await AddAsync(new DateTime(2026, 1, 1, 0, 30, 0), "trail_running", name: "New Year", km: 8);
        await AddAsync(new DateTime(2026, 8, 22, 10, 0, 0), "obstacle_run", race: true, name: "Buffel run", km: 7.2, minutes: 120);
        await AddAsync(new DateTime(2026, 10, 9, 23, 59, 0), "treadmill_running", name: "Late treadmill", km: 5, minutes: 30);
        await AddAsync(new DateTime(2026, 10, 10, 7, 0, 0), name: "Morning");
    }

    static Task<RunListData> Load(RunListQueries queries, RunFilter? filter = null, int page = 1) =>
        queries.LoadAsync(filter ?? new RunFilter(), page, Ct);

    [Fact]
    public async Task Without_filters_every_run_is_listed_newest_first()
    {
        await AddRunsAsync();

        var data = await Load(queries);

        Assert.Equal(["Morning", "Late treadmill", "Buffel run", "New Year", "Eindhoven"], data.Runs.Select(r => r.Name));
        Assert.Equal((1, new DateOnly(2025, 10, 12), "running", "Eindhoven", 21100d, 115 * 60d),
            (data.Runs[^1].Id, data.Runs[^1].Date, data.Runs[^1].TypeKey, data.Runs[^1].Name, data.Runs[^1].DistanceMeters, data.Runs[^1].DurationSeconds));
        Assert.Equal(new RunFilter(), data.Filter);
    }

    [Fact]
    public async Task Rows_carry_the_runs_metrics()
    {
        await using (var context = await db.CreateDbContextAsync(Ct))
        {
            var run = NewRun(new DateTime(2026, 10, 17, 10, 0, 0));
            run.AverageSpeedMps = 3.13;
            run.MaxSpeedMps = 4.5;
            run.AverageHeartRate = 162;
            run.MaxHeartRate = 181;
            run.AverageCadenceSpm = 171;
            run.MaxCadenceSpm = 186;
            run.StrideLengthCm = 109.7;
            run.Calories = 512;
            run.ElevationGainMeters = 12;
            run.ElevationLossMeters = 14;
            context.Runs.Add(run);
            context.Runs.Add(NewRun(new DateTime(2026, 10, 16, 18, 0, 0), "treadmill_running"));
            await context.SaveChangesAsync(Ct);
        }

        var data = await Load(queries);

        Assert.Equal(new RunRowMetrics(3.13, 4.5, 162, 181, 171, 186, 109.7, 512, 12, 14), data.Runs[0].Metrics);
        Assert.Equal(new RunRowMetrics(null, null, null, null, null, null, null, null, null, null), data.Runs[1].Metrics);
    }

    [Fact]
    public async Task Types_and_races_are_the_choices_on_offer()
    {
        await AddRunsAsync();

        var data = await Load(queries);

        Assert.Equal(["obstacle_run", "running", "trail_running", "treadmill_running"], data.TypeKeys);
        Assert.True(data.HasRaces);
    }

    [Fact]
    public async Task Totals_cover_the_selection()
    {
        await AddRunsAsync();

        var data = await Load(queries, new RunFilter(TypeKey: "running"));

        Assert.Equal((2, 31100d, 165 * 60d), (data.Count, data.DistanceMeters, data.DurationSeconds));
    }

    [Fact]
    public async Task Dates_are_inclusive_by_the_runs_local_date()
    {
        await AddRunsAsync();

        var data = await Load(queries, new RunFilter(new DateOnly(2026, 1, 1), new DateOnly(2026, 10, 9)));

        Assert.Equal(["Late treadmill", "Buffel run", "New Year"], data.Runs.Select(r => r.Name));
    }

    [Fact]
    public async Task A_date_range_can_be_open_ended()
    {
        await AddRunsAsync();

        Assert.Equal(["Morning", "Late treadmill"], (await Load(queries, new RunFilter(From: new DateOnly(2026, 10, 9)))).Runs.Select(r => r.Name));
        Assert.Equal(["Eindhoven"], (await Load(queries, new RunFilter(To: new DateOnly(2025, 12, 31)))).Runs.Select(r => r.Name));
    }

    [Fact]
    public async Task Dates_the_wrong_way_round_are_swapped()
    {
        await AddRunsAsync();

        var data = await Load(queries, new RunFilter(new DateOnly(2026, 10, 9), new DateOnly(2026, 1, 1)));

        Assert.Equal(new RunFilter(new DateOnly(2026, 1, 1), new DateOnly(2026, 10, 9)), data.Filter);
        Assert.Equal(3, data.Count);
    }

    [Fact]
    public async Task Rows_carry_garmins_training_effect_label()
    {
        await using (var context = await db.CreateDbContextAsync(Ct))
        {
            var run = NewRun(new DateTime(2026, 10, 17, 10, 0, 0));
            run.TrainingEffectLabel = "TEMPO";
            context.Runs.Add(run);
            await context.SaveChangesAsync(Ct);
        }

        Assert.Equal("TEMPO", (await Load(queries)).Runs.Single().TrainingEffectLabel);
    }

    [Fact]
    public async Task Rows_say_whether_the_run_was_a_race()
    {
        await AddRunsAsync();

        var data = await Load(queries);

        Assert.Equal(["Buffel run", "Eindhoven"], data.Runs.Where(r => r.IsRace).Select(r => r.Name));
    }

    [Fact]
    public async Task Races_only()
    {
        await AddRunsAsync();

        var data = await Load(queries, new RunFilter(RacesOnly: true));

        Assert.Equal(["Buffel run", "Eindhoven"], data.Runs.Select(r => r.Name));
        Assert.True(data.Filter.RacesOnly);
    }

    [Fact]
    public async Task Filters_combine()
    {
        await AddRunsAsync();

        var data = await Load(queries, new RunFilter(new DateOnly(2026, 1, 1), null, "obstacle_run", true));

        Assert.Equal(["Buffel run"], data.Runs.Select(r => r.Name));
    }

    [Fact]
    public async Task A_filter_that_matches_nothing_shows_an_empty_list()
    {
        await AddRunsAsync();

        var data = await Load(queries, new RunFilter(TypeKey: "trail_running", RacesOnly: true));

        Assert.Empty(data.Runs);
        Assert.Equal((0, 0d, 0d, 1, 1), (data.Count, data.DistanceMeters, data.DurationSeconds, data.Page, data.PageCount));
    }

    [Fact]
    public async Task An_unknown_type_is_dropped()
    {
        await AddRunsAsync();

        var data = await Load(queries, new RunFilter(TypeKey: "swimming"));

        Assert.Null(data.Filter.TypeKey);
        Assert.Equal(5, data.Count);
    }

    [Fact]
    public async Task Races_only_is_dropped_when_there_are_no_races()
    {
        await AddAsync(new DateTime(2026, 10, 10, 7, 0, 0));

        var data = await Load(queries, new RunFilter(RacesOnly: true));

        Assert.False(data.HasRaces);
        Assert.False(data.Filter.RacesOnly);
        Assert.Single(data.Runs);
    }

    async Task AddManyAsync(int count)
    {
        await using var context = await db.CreateDbContextAsync(Ct);
        var start = new DateTime(2026, 1, 1, 8, 0, 0);
        for (var i = 0; i < count; i++)
            context.Runs.Add(NewRun(start.AddDays(i), name: $"Run {i + 1}"));
        await context.SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task Pages_hold_fifty_runs()
    {
        await AddManyAsync(RunListQueries.PageSize * 2 + 1);

        var first = await Load(queries);
        var third = await Load(queries, page: 3);

        Assert.Equal((1, 3, 50), (first.Page, first.PageCount, first.Runs.Count));
        Assert.Equal(("Run 101", "Run 52"), (first.Runs[0].Name, first.Runs[^1].Name));
        Assert.Equal(["Run 1"], third.Runs.Select(r => r.Name));
        Assert.Equal(101, third.Count);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(9, 2)]
    public async Task A_page_out_of_range_shows_the_nearest_one(int page, int shown)
    {
        await AddManyAsync(RunListQueries.PageSize + 1);

        Assert.Equal(shown, (await Load(queries, page: page)).Page);
    }

    [Fact]
    public async Task Runs_starting_at_the_same_time_keep_their_order_across_pages()
    {
        await using (var context = await db.CreateDbContextAsync(Ct))
        {
            for (var i = 0; i < RunListQueries.PageSize + 10; i++)
                context.Runs.Add(NewRun(new DateTime(2026, 5, 1, 8, 0, 0), name: $"Run {i + 1}"));
            await context.SaveChangesAsync(Ct);
        }

        var names = (await Load(queries)).Runs.Concat((await Load(queries, page: 2)).Runs).Select(r => r.Name).ToList();

        Assert.Equal(RunListQueries.PageSize + 10, names.Distinct().Count());
    }

    [Fact]
    public async Task Without_runs_there_is_nothing_to_choose()
    {
        var data = await Load(queries);

        Assert.Empty(data.TypeKeys);
        Assert.False(data.HasRaces);
        Assert.Empty(data.Runs);
        Assert.Equal(1, data.PageCount);
    }
}
