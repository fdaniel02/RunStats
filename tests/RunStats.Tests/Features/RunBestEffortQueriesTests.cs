using Microsoft.EntityFrameworkCore.Infrastructure;
using RunStats.Data;
using RunStats.Features.Runs;
using RunStats.Tests.Data;

namespace RunStats.Tests.Features;

[Collection("SqlServer")]
public sealed class RunBestEffortQueriesTests(SqlServerFixture sqlServer) : IAsyncLifetime
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    PooledDbContextFactory<AppDbContext> db = null!;
    RunBestEffortQueries queries = null!;
    long nextId = 1;

    public async ValueTask InitializeAsync()
    {
        db = await MigratedDatabase.CreateAsync(sqlServer);
        queries = new RunBestEffortQueries(db);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    async Task<long> AddAsync(DateTime startLocal, params (int TypeId, double Seconds)[] efforts)
    {
        var id = nextId++;
        await using var context = await db.CreateDbContextAsync(Ct);
        context.Runs.Add(new Run
        {
            Id = id,
            Name = "Run",
            TypeKey = "running",
            TypeId = 1,
            ParentTypeId = 17,
            StartLocal = startLocal,
            StartUtc = DateTime.SpecifyKind(startLocal.AddHours(-1), DateTimeKind.Utc),
            DistanceMeters = 10000,
            DurationSeconds = 3000,
            BestEfforts = [.. efforts.Select(e => new RunBestEffort { TypeId = e.TypeId, Seconds = e.Seconds })]
        });
        await context.SaveChangesAsync(Ct);
        return id;
    }

    static readonly DateTime Krollenloop = new(2026, 11, 1, 11, 31, 30);

    [Fact]
    public async Task A_slower_run_is_compared_with_the_pr_and_the_year_best()
    {
        var pr = await AddAsync(new DateTime(2025, 10, 12, 12, 0, 0), (3, 1400));
        var yearBest = await AddAsync(new DateTime(2026, 9, 27, 13, 55, 0), (3, 1429));
        await AddAsync(new DateTime(2026, 9, 6, 10, 0, 0), (3, 1600));
        var run = await AddAsync(Krollenloop, (3, 1502));

        var effort = Assert.Single(await queries.LoadAsync(run, Ct));

        Assert.Equal(3, effort.TypeId);
        Assert.Equal(1502, effort.Seconds);
        Assert.Equal(new EffortComparison(new OtherEffort(1400, pr, new DateOnly(2025, 10, 12)), false), effort.Pr);
        Assert.Equal(new EffortComparison(new OtherEffort(1429, yearBest, new DateOnly(2026, 9, 27)), false), effort.YearBest);
    }

    [Fact]
    public async Task The_pr_counts_later_runs_and_holding_it_compares_with_the_next_fastest()
    {
        var run = await AddAsync(new DateTime(2026, 3, 1, 10, 0, 0), (1, 270));
        var later = await AddAsync(new DateTime(2026, 7, 22, 19, 0, 0), (1, 275));
        await AddAsync(new DateTime(2025, 7, 1, 19, 0, 0), (1, 290));

        var effort = Assert.Single(await queries.LoadAsync(run, Ct));

        Assert.Equal(new EffortComparison(new OtherEffort(275, later, new DateOnly(2026, 7, 22)), true), effort.Pr);
        Assert.Equal(effort.Pr, effort.YearBest);
    }

    [Fact]
    public async Task The_year_best_goes_by_the_runs_own_year()
    {
        var pr = await AddAsync(new DateTime(2025, 12, 31, 23, 30, 0), (1, 260));
        var run = await AddAsync(new DateTime(2026, 1, 1, 0, 30, 0), (1, 280));

        var effort = Assert.Single(await queries.LoadAsync(run, Ct));

        Assert.False(effort.Pr.IsBest);
        Assert.Equal(pr, effort.Pr.Other!.RunId);
        Assert.Equal(new EffortComparison(null, true), effort.YearBest);
    }

    [Fact]
    public async Task On_a_tie_the_earlier_run_holds_the_best()
    {
        var earlier = await AddAsync(new DateTime(2026, 5, 1, 10, 0, 0), (1, 280));
        var later = await AddAsync(new DateTime(2026, 6, 1, 10, 0, 0), (1, 280));

        Assert.True(Assert.Single(await queries.LoadAsync(earlier, Ct)).Pr.IsBest);
        var effort = Assert.Single(await queries.LoadAsync(later, Ct));
        Assert.False(effort.Pr.IsBest);
        Assert.Equal(earlier, effort.Pr.Other!.RunId);
    }

    [Fact]
    public async Task Each_distance_of_the_run_in_record_order_and_only_against_the_same_distance()
    {
        await AddAsync(new DateTime(2026, 5, 1, 10, 0, 0), (1, 250), (4, 2900));
        var run = await AddAsync(Krollenloop, (4, 3058), (1, 280), (3, 1502));

        var efforts = await queries.LoadAsync(run, Ct);

        Assert.Equal([1, 3, 4], efforts.Select(e => e.TypeId));
        Assert.Equal(new EffortComparison(null, true), efforts[1].Pr);
        Assert.Equal(2900, efforts[2].Pr.Other!.Seconds);
    }

    [Fact]
    public async Task A_run_without_efforts_or_an_unknown_run_has_none()
    {
        var treadmill = await AddAsync(Krollenloop);
        await AddAsync(new DateTime(2026, 5, 1, 10, 0, 0), (1, 250));

        Assert.Empty(await queries.LoadAsync(treadmill, Ct));
        Assert.Empty(await queries.LoadAsync(999, Ct));
    }
}
