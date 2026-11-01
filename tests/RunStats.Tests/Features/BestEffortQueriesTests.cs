using Microsoft.EntityFrameworkCore.Infrastructure;
using RunStats.Data;
using RunStats.Features.Trends;
using RunStats.Tests.Data;

namespace RunStats.Tests.Features;

[Collection("SqlServer")]
public sealed class BestEffortQueriesTests(SqlServerFixture sqlServer) : IAsyncLifetime
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Tuesday 27 October 2026, 10:00 in Amsterdam.
    readonly FixedTimeProvider time = new(new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero));
    PooledDbContextFactory<AppDbContext> db = null!;
    BestEffortQueries queries = null!;
    long nextId = 1;

    public async ValueTask InitializeAsync()
    {
        db = await MigratedDatabase.CreateAsync(sqlServer);
        queries = new BestEffortQueries(db, new Clock(TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam"), time));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    async Task<long> AddAsync(DateTime startLocal, string name = "Run", params (int TypeId, double Seconds)[] efforts)
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
            StartLocal = startLocal,
            StartUtc = DateTime.SpecifyKind(startLocal.AddHours(-1), DateTimeKind.Utc),
            DistanceMeters = 10_000,
            DurationSeconds = 3000,
            BestEfforts = [.. efforts.Select(e => new RunBestEffort { TypeId = e.TypeId, Seconds = e.Seconds })]
        });
        await context.SaveChangesAsync(Ct);
        return id;
    }

    [Fact]
    public async Task Without_efforts_there_are_no_series()
    {
        await AddAsync(new DateTime(2026, 10, 1, 9, 0, 0));

        Assert.Empty(await queries.LoadAsync(Ct));
    }

    [Fact]
    public async Task Each_month_has_its_fastest_effort_from_the_first_month_to_this_one()
    {
        await AddAsync(new DateTime(2026, 7, 31, 23, 30, 0), "Late July", (3, 1500));
        var faster = await AddAsync(new DateTime(2026, 7, 10, 9, 0, 0), "Tempo", (3, 1470));
        var august = await AddAsync(new DateTime(2026, 8, 1, 0, 30, 0), "Early August", (3, 1490));

        var series = Assert.Single(await queries.LoadAsync(Ct));

        Assert.Equal(3, series.TypeId);
        Assert.Equal(
        [
            new EffortMonth(new DateOnly(2026, 7, 1), new MonthEffort(1470, faster, new DateOnly(2026, 7, 10), "Tempo")),
            new EffortMonth(new DateOnly(2026, 8, 1), new MonthEffort(1490, august, new DateOnly(2026, 8, 1), "Early August")),
            new EffortMonth(new DateOnly(2026, 9, 1), null),
            new EffortMonth(new DateOnly(2026, 10, 1), null)
        ], series.Months);
        Assert.Equal(faster, series.Best.RunId);
    }

    [Fact]
    public async Task A_tie_goes_to_the_earlier_run()
    {
        var first = await AddAsync(new DateTime(2026, 9, 3, 9, 0, 0), efforts: (1, 280));
        await AddAsync(new DateTime(2026, 9, 20, 9, 0, 0), efforts: (1, 280));
        await AddAsync(new DateTime(2026, 10, 20, 9, 0, 0), efforts: (1, 280));

        var series = Assert.Single(await queries.LoadAsync(Ct));

        Assert.Equal(first, series.Months[0].Effort!.RunId);
        Assert.Equal(first, series.Best.RunId);
    }

    [Fact]
    public async Task Distances_share_the_months_and_come_in_record_order()
    {
        await AddAsync(new DateTime(2026, 9, 5, 9, 0, 0), efforts: [(4, 3100), (1, 290)]);
        await AddAsync(new DateTime(2026, 10, 5, 9, 0, 0), efforts: (1, 285));

        var series = await queries.LoadAsync(Ct);

        Assert.Equal([1, 4], series.Select(s => s.TypeId));
        Assert.All(series, s => Assert.Equal([new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1)], s.Months.Select(m => m.Month)));
        Assert.Null(series[1].Months[1].Effort);
        Assert.Equal(285, series[0].Best.Seconds);
    }
}
