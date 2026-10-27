using Microsoft.EntityFrameworkCore.Infrastructure;
using RunStats.Data;
using RunStats.Features.Runs;
using RunStats.Tests.Data;

namespace RunStats.Tests.Features;

[Collection("SqlServer")]
public sealed class RunDetailsQueriesTests(SqlServerFixture sqlServer) : IAsyncLifetime
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    PooledDbContextFactory<AppDbContext> db = null!;
    RunDetailsQueries queries = null!;

    public async ValueTask InitializeAsync()
    {
        db = await MigratedDatabase.CreateAsync(sqlServer);
        queries = new RunDetailsQueries(db);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    async Task AddAsync(long id, string name, bool track, params int[] lapIndexes)
    {
        await using var context = await db.CreateDbContextAsync(Ct);
        var start = new DateTime(2026, 10, 17, 13, 7, 0, DateTimeKind.Utc);
        context.Runs.Add(new Run
        {
            Id = id,
            Name = name,
            TypeKey = "running",
            TypeId = 1,
            IsRace = true,
            StartUtc = start,
            StartLocal = start.AddHours(2),
            DistanceMeters = 7500,
            DurationSeconds = 2397,
            Vo2Max = 50,
            Laps = [.. lapIndexes.Select(i => new RunLap { LapIndex = i, StartUtc = start.AddMinutes(5 * i), DistanceMeters = 1000, DurationSeconds = 300 })],
            Track = track ? new RunTrack { EncodedPolyline = $"line{id}", PointCount = 2 } : null
        });
        await context.SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task Loads_the_run_its_laps_in_order_and_its_route()
    {
        await AddAsync(1, "Amsterdam Running", track: true, 2, 3, 1);
        await AddAsync(2, "Other", track: true, 1);

        var details = await queries.LoadAsync(1, Ct);

        Assert.NotNull(details);
        Assert.Equal(("Amsterdam Running", true, 50d, new DateTime(2026, 10, 17, 15, 7, 0)),
            (details.Run.Name, details.Run.IsRace, details.Run.Vo2Max!.Value, details.Run.StartLocal));
        Assert.Equal([1, 2, 3], details.Laps.Select(l => l.LapIndex));
        Assert.All(details.Laps, l => Assert.Equal(1, l.RunId));
        Assert.Equal("line1", details.Polyline);
    }

    [Fact]
    public async Task A_run_without_gps_has_no_route()
    {
        await AddAsync(1, "Treadmill", track: false, 1);

        Assert.Null((await queries.LoadAsync(1, Ct))!.Polyline);
    }

    [Fact]
    public async Task A_run_without_laps_has_none()
    {
        await AddAsync(1, "Short", track: false);

        Assert.Empty((await queries.LoadAsync(1, Ct))!.Laps);
    }

    [Fact]
    public async Task An_unknown_run_is_null()
    {
        await AddAsync(1, "Amsterdam Running", track: true, 1);

        Assert.Null(await queries.LoadAsync(99, Ct));
    }
}
