using Microsoft.EntityFrameworkCore.Infrastructure;
using RunStats.Data;
using RunStats.Features.Heatmap;
using RunStats.Tests.Data;

namespace RunStats.Tests.Features;

[Collection("SqlServer")]
public sealed class HeatmapQueriesTests(SqlServerFixture sqlServer) : IAsyncLifetime
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    PooledDbContextFactory<AppDbContext> db = null!;
    HeatmapQueries queries = null!;
    long nextId = 1;

    public async ValueTask InitializeAsync()
    {
        db = await MigratedDatabase.CreateAsync(sqlServer);
        queries = new HeatmapQueries(db);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    async Task AddAsync(DateTime startLocal, string typeKey = "running", bool track = true, string name = "Run", double km = 10)
    {
        var id = nextId++;
        await using var context = await db.CreateDbContextAsync(Ct);
        context.Runs.Add(new Run
        {
            Id = id,
            Name = name,
            TypeKey = typeKey,
            TypeId = 1,
            StartLocal = startLocal,
            StartUtc = DateTime.SpecifyKind(startLocal.AddHours(-1), DateTimeKind.Utc),
            DistanceMeters = km * 1000,
            DurationSeconds = 3000,
            Track = track ? new RunTrack { EncodedPolyline = $"line{id}", PointCount = 2 } : null
        });
        await context.SaveChangesAsync(Ct);
    }

    async Task AddRunsAsync()
    {
        await AddAsync(new DateTime(2025, 10, 12, 9, 0, 0), name: "Eindhoven", km: 21.1);
        await AddAsync(new DateTime(2026, 1, 1, 0, 30, 0), "trail_running", name: "New Year");
        await AddAsync(new DateTime(2026, 8, 22, 10, 0, 0), "obstacle_run", name: "Buffel run", km: 7.2);
        await AddAsync(new DateTime(2026, 10, 9, 18, 0, 0), "treadmill_running", track: false);
        await AddAsync(new DateTime(2024, 5, 5, 9, 0, 0), "treadmill_running", track: false);
    }

    [Fact]
    public async Task All_time_has_every_run_with_a_track_oldest_first()
    {
        await AddRunsAsync();

        var data = await queries.LoadAsync(null, null, Ct);

        Assert.Equal(["Eindhoven", "New Year", "Buffel run"], data.Tracks.Select(t => t.Name));
        Assert.Equal(new TrackRow(1, new DateOnly(2025, 10, 12), "running", "Eindhoven", 21100, "line1"), data.Tracks[0]);
        Assert.Null(data.Year);
        Assert.Null(data.TypeKey);
    }

    [Fact]
    public async Task Years_and_types_are_those_with_tracks()
    {
        await AddRunsAsync();

        var data = await queries.LoadAsync(null, null, Ct);

        Assert.Equal([2026, 2025], data.Years);
        Assert.Equal(["obstacle_run", "running", "trail_running"], data.TypeKeys);
    }

    [Fact]
    public async Task A_year_is_the_runs_local_year()
    {
        await AddRunsAsync();

        var data = await queries.LoadAsync(2026, null, Ct);

        Assert.Equal(2026, data.Year);
        Assert.Equal(["New Year", "Buffel run"], data.Tracks.Select(t => t.Name));
    }

    [Fact]
    public async Task Year_and_type_together()
    {
        await AddRunsAsync();

        var data = await queries.LoadAsync(2026, "obstacle_run", Ct);

        Assert.Equal(("obstacle_run", 2026), (data.TypeKey, data.Year));
        Assert.Equal(["Buffel run"], data.Tracks.Select(t => t.Name));
    }

    [Theory]
    [InlineData(2019, null)]
    [InlineData(null, "treadmill_running")]
    [InlineData(null, "swimming")]
    public async Task Filters_that_match_nothing_on_offer_are_dropped(int? year, string? typeKey)
    {
        await AddRunsAsync();

        var data = await queries.LoadAsync(year, typeKey, Ct);

        Assert.Null(data.Year);
        Assert.Null(data.TypeKey);
        Assert.Equal(3, data.Tracks.Count);
    }

    [Fact]
    public async Task Without_tracks_there_is_nothing_to_choose()
    {
        await AddAsync(new DateTime(2026, 10, 9, 18, 0, 0), "treadmill_running", track: false);

        var data = await queries.LoadAsync(null, null, Ct);

        Assert.Empty(data.Years);
        Assert.Empty(data.TypeKeys);
        Assert.Empty(data.Tracks);
    }
}
