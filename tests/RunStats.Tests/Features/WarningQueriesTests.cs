using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Options;
using RunStats.Data;
using RunStats.Features.Warnings;
using RunStats.Tests.Data;

namespace RunStats.Tests.Features;

[Collection("SqlServer")]
public sealed class WarningQueriesTests(SqlServerFixture sqlServer) : IAsyncLifetime
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;
    static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    // Monday 28 September 2026, 20:00 in Amsterdam (summer time, UTC+2).
    readonly FixedTimeProvider time = new(new DateTimeOffset(2026, 9, 28, 18, 0, 0, TimeSpan.Zero));
    PooledDbContextFactory<AppDbContext> db = null!;
    WarningQueries queries = null!;
    long nextId = 1;

    public async ValueTask InitializeAsync()
    {
        db = await MigratedDatabase.CreateAsync(sqlServer);
        queries = new WarningQueries(db, Options.Create(new WarningSettings()), new Clock(Amsterdam, time), time);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // Fast minutes at 5:00/km, the rest at 5:35/km.
    async Task AddAsync(DateTime startUtc, double minutes, double fastMinutes = 0)
    {
        await using var context = await db.CreateDbContextAsync(Ct);
        var id = nextId++;
        context.Runs.Add(new Run
        {
            Id = id,
            Name = "Run",
            TypeKey = "running",
            TypeId = 1,
            ParentTypeId = 17,
            StartUtc = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc),
            StartLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(startUtc, DateTimeKind.Utc), Amsterdam),
            DistanceMeters = minutes * 180,
            DurationSeconds = minutes * 60,
            PaceBands = [Band(335, minutes - fastMinutes), Band(300, fastMinutes)]
        });
        await context.SaveChangesAsync(Ct);
    }

    static RunPaceBand Band(short pace, double minutes) =>
        new() { PaceSecondsPerKm = pace, Seconds = (int)(minutes * 60), FlatSeconds = (int)(minutes * 60), CadenceSpm = 174 };

    async Task AddUsualWeeksAsync()
    {
        for (var week = 1; week <= 4; week++)
            await AddAsync(new DateTime(2026, 9, 26, 7, 0, 0).AddDays(-7 * week), 45, fastMinutes: 5);
    }

    [Fact]
    public async Task Active_warnings_come_from_the_stored_runs_and_bands()
    {
        await AddUsualWeeksAsync();
        await AddAsync(new DateTime(2026, 9, 24, 15, 0, 0), 61, fastMinutes: 38);
        await AddAsync(new DateTime(2026, 9, 27, 11, 0, 0), 57, fastMinutes: 30);

        var warnings = await queries.ActiveAsync(Ct);

        Assert.Equal([WarningKind.FastJump, WarningKind.HardRunsBackToBack], warnings.Select(w => w.Kind));
        Assert.Equal("Hard runs on 2026-09-24 and 2026-09-27, 68 h apart.", warnings[1].Message);
    }

    [Fact]
    public async Task No_runs_no_active_warnings() => Assert.Empty(await queries.ActiveAsync(Ct));

    [Fact]
    public async Task Each_day_is_evaluated_at_its_end_and_quiet_days_are_left_out()
    {
        await AddUsualWeeksAsync();
        // 23:30 in Amsterdam on Thursday 24 September: counts for that day, not the next.
        await AddAsync(new DateTime(2026, 9, 24, 21, 30, 0), 60, fastMinutes: 50);

        var days = await queries.DailyAsync(new DateOnly(2026, 9, 22), new DateOnly(2026, 10, 3), Ct);

        // The fast week lasts 7 days: from the 24th up to and including the 30th.
        Assert.Equal(Enumerable.Range(24, 7).Select(d => new DateOnly(2026, 9, d)), days.Select(d => d.Date));
        Assert.All(days, d => Assert.Equal(WarningKind.FastJump, Assert.Single(d.Warnings).Kind));
    }

    [Fact]
    public async Task Warnings_at_several_moments_come_in_their_order()
    {
        await AddUsualWeeksAsync();
        await AddAsync(new DateTime(2026, 9, 24, 15, 0, 0), 60, fastMinutes: 50);

        var warnings = await queries.AtAsync(
            [new DateTime(2026, 9, 25, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 24, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc)], Ct);

        Assert.Equal([[WarningKind.FastJump], [], []], warnings.Select(w => w.Select(x => x.Kind).ToList()));
    }

    [Fact]
    public async Task No_moments_no_warnings() => Assert.Empty(await queries.AtAsync([], Ct));
}
