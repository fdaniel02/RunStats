using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using RunStats.Data;
using RunStats.Sync;
using RunStats.Tests.Data;
using static RunStats.Tests.Sync.FakeNotionRaces;

namespace RunStats.Tests.Sync;

[Collection("SqlServer")]
public sealed class UpcomingRacesSyncTests(SqlServerFixture sqlServer) : IAsyncLifetime
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    readonly FakeNotionRaces notion = new();
    readonly FakeLogger<UpcomingRacesSync> logger = new();
    PooledDbContextFactory<AppDbContext> db = null!;
    UpcomingRacesSync sync = null!;

    public async ValueTask InitializeAsync()
    {
        db = await MigratedDatabase.CreateAsync(sqlServer);
        var clock = new Clock(TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam"), new FixedTimeProvider(new DateTimeOffset(2026, 11, 5, 23, 30, 0, TimeSpan.Zero)));
        sync = new UpcomingRacesSync(notion, db, clock, logger);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    async Task<List<UpcomingRace>> StoredAsync()
    {
        await using var context = await db.CreateDbContextAsync(Ct);
        return await context.UpcomingRaces.AsNoTracking().OrderBy(r => r.Name).ToListAsync(Ct);
    }

    [Fact]
    public async Task A_new_race_is_stored_from_todays_date_in_the_users_time_zone()
    {
        notion.Races.Add(Race(start: new TimeOnly(13, 30), link: "https://example.com/", target: "1:20:00"));

        await sync.UpdateAsync(1, Ct);

        Assert.Equal(new DateOnly(2026, 11, 6), notion.RequestedFrom);
        var race = Assert.Single(await StoredAsync());
        Assert.Equal(
            ("Zandvoort", new DateOnly(2027, 3, 21), new TimeOnly(13, 30), 16100.0, "Normal", true, "Zandvoort", "https://example.com/", 4800.0),
            (race.Name, race.Date, race.StartTime, race.DistanceMeters, race.Type, race.IsRegistered, race.City, race.Link, race.TargetSeconds));
    }

    [Fact]
    public async Task A_changed_race_is_updated_and_a_removed_one_goes()
    {
        var kept = Guid.NewGuid();
        notion.Races.AddRange([Race("Kept", kept, km: 10, registered: false), Race("Removed")]);
        await sync.UpdateAsync(1, Ct);

        notion.Races.Clear();
        notion.Races.AddRange([Race("Renamed", kept, km: 21.1, registered: true, target: "1:45:00"), Race("New")]);
        await sync.UpdateAsync(2, Ct);

        var stored = await StoredAsync();
        Assert.Equal(["New", "Renamed"], stored.Select(r => r.Name));
        var renamed = stored.Single(r => r.NotionPageId == kept);
        Assert.Equal((21100.0, true, 6300.0), (renamed.DistanceMeters, renamed.IsRegistered, renamed.TargetSeconds));
    }

    [Fact]
    public async Task No_races_in_Notion_empties_the_copy()
    {
        notion.Races.Add(Race());
        await sync.UpdateAsync(1, Ct);

        notion.Races.Clear();
        await sync.UpdateAsync(2, Ct);

        Assert.Empty(await StoredAsync());
    }

    [Fact]
    public async Task An_empty_distance_stays_empty()
    {
        notion.Races.Add(Race(km: null));

        await sync.UpdateAsync(1, Ct);

        Assert.Null(Assert.Single(await StoredAsync()).DistanceMeters);
    }

    [Fact]
    public async Task An_unreadable_target_is_logged_and_the_race_stays()
    {
        notion.Races.Add(Race(target: "fast"));

        await sync.UpdateAsync(1, Ct);

        Assert.Null(Assert.Single(await StoredAsync()).TargetSeconds);
        Assert.Contains(logger.Collector.GetSnapshot(), l => l.Level == LogLevel.Warning && l.Id.Id == 26 && l.Message.Contains("fast", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Long_texts_are_shortened_to_fit()
    {
        notion.Races.Add(Race(new string('x', 300), city: new string('y', 150), type: new string('z', 80)));

        await sync.UpdateAsync(1, Ct);

        var race = Assert.Single(await StoredAsync());
        Assert.Equal((200, 100, 50), (race.Name.Length, race.City!.Length, race.Type!.Length));
    }

    [Theory]
    [InlineData(typeof(HttpRequestException))]
    [InlineData(typeof(JsonException))]
    [InlineData(typeof(KeyNotFoundException))]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(TaskCanceledException))]
    public async Task When_Notion_fails_the_last_copy_stays_and_a_warning_is_logged(Type failure)
    {
        notion.Races.Add(Race());
        await sync.UpdateAsync(1, Ct);

        notion.Failure = (Exception)Activator.CreateInstance(failure)!;
        await sync.UpdateAsync(2, Ct);

        Assert.Single(await StoredAsync());
        Assert.Contains(logger.Collector.GetSnapshot(), l => l.Level == LogLevel.Warning && l.Id.Id == 24);
    }

    [Fact]
    public async Task A_cancelled_sync_stays_cancelled()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        notion.Failure = new OperationCanceledException(cancelled.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(() => sync.UpdateAsync(1, cancelled.Token));
    }

    [Fact]
    public async Task Without_settings_Notion_isnt_called_and_the_copy_stays()
    {
        notion.Races.Add(Race());
        await sync.UpdateAsync(1, Ct);

        notion.IsConfigured = false;
        notion.Races.Clear();
        await sync.UpdateAsync(2, Ct);

        Assert.Single(await StoredAsync());
        Assert.Contains(logger.Collector.GetSnapshot(), l => l.Id.Id == 25);
    }
}
