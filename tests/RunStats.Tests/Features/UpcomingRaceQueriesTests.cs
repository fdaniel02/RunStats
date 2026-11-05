using Microsoft.EntityFrameworkCore.Infrastructure;
using RunStats.Data;
using RunStats.Features.Races;
using RunStats.Tests.Data;

namespace RunStats.Tests.Features;

[Collection("SqlServer")]
public sealed class UpcomingRaceQueriesTests(SqlServerFixture sqlServer) : IAsyncLifetime
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    PooledDbContextFactory<AppDbContext> db = null!;
    UpcomingRaceQueries queries = null!;

    public async ValueTask InitializeAsync()
    {
        db = await MigratedDatabase.CreateAsync(sqlServer);
        var clock = new Clock(TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam"), new FixedTimeProvider(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero)));
        queries = new UpcomingRaceQueries(db, clock);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    async Task SeedAsync(params object[] rows)
    {
        await using var context = await db.CreateDbContextAsync(Ct);
        context.AddRange(rows);
        await context.SaveChangesAsync(Ct);
    }

    static UpcomingRace Race(string name, DateOnly date, TimeOnly? start = null, double? meters = 10000, string? type = "Normal") =>
        new() { NotionPageId = Guid.NewGuid(), Name = name, Date = date, StartTime = start, DistanceMeters = meters, Type = type, IsRegistered = true };

    [Fact]
    public async Task Without_races_it_is_empty()
    {
        Assert.Empty(await queries.LoadAsync(Ct));
    }

    [Fact]
    public async Task Races_come_in_date_order_from_today_on()
    {
        await SeedAsync(
            Race("Later", new DateOnly(2027, 3, 21)),
            Race("Past", new DateOnly(2026, 11, 4)),
            Race("Today, late", new DateOnly(2026, 11, 5), new TimeOnly(18, 0)),
            Race("Today, early", new DateOnly(2026, 11, 5), new TimeOnly(9, 0)));

        var races = await queries.LoadAsync(Ct);

        Assert.Equal(["Today, early", "Today, late", "Later"], races.Select(r => r.Name));
    }

    [Fact]
    public async Task Races_get_predictions_from_Garmins_latest()
    {
        await SeedAsync(
            new RacePrediction { TypeId = 3, Seconds = 1310, PredictedOn = new DateOnly(2026, 11, 3) },
            new RacePrediction { TypeId = 4, Seconds = 2806, PredictedOn = new DateOnly(2026, 11, 4) },
            Race("10 km", new DateOnly(2027, 1, 1)),
            Race("7 km", new DateOnly(2027, 1, 2), meters: 7000),
            Race("Trail", new DateOnly(2027, 1, 3), type: "Trail"),
            Race("No distance", new DateOnly(2027, 1, 4), meters: null));

        var races = await queries.LoadAsync(Ct);

        Assert.Equal(new PredictedFinish(2806, false), races[0].Prediction);
        Assert.True(races[1].Prediction!.IsEstimate);
        Assert.Null(races[2].Prediction);
        Assert.Null(races[3].Prediction);
    }
}
