using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;
using RunStats.Data;
using RunStats.Sync;
using RunStats.Tests.Data;

namespace RunStats.Tests.Sync;

[Collection("SqlServer")]
public sealed class SyncStatusQueriesTests(SqlServerFixture sqlServer) : IAsyncLifetime
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;
    static readonly DateTime Evening = new(2026, 10, 26, 19, 0, 0, DateTimeKind.Utc);

    readonly FixedTimeProvider time = new(new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero));
    readonly ServiceProvider services = new ServiceCollection().AddMetrics().BuildServiceProvider();
    PooledDbContextFactory<AppDbContext> db = null!;
    SyncCoordinator coordinator = null!;
    SyncStatusQueries queries = null!;

    public async ValueTask InitializeAsync()
    {
        db = await MigratedDatabase.CreateAsync(sqlServer);
        coordinator = new SyncCoordinator(db, new SyncMetrics(services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>()), time,
            new FakeLogger<SyncCoordinator>());
        queries = new SyncStatusQueries(coordinator, db);
    }

    public async ValueTask DisposeAsync()
    {
        coordinator.Dispose();
        await services.DisposeAsync();
    }

    async Task AddAsync(SyncStatus status, DateTime finishedAtUtc, string? error = null)
    {
        await using var context = await db.CreateDbContextAsync(Ct);
        context.SyncRuns.Add(new SyncRun
        {
            Status = status, StartedAtUtc = finishedAtUtc.AddMinutes(-2), FinishedAtUtc = finishedAtUtc, Error = error
        });
        await context.SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task Without_syncs_there_is_nothing_to_show()
    {
        Assert.Equal(new SyncState(null, null, null), await queries.LoadAsync(Ct));
        Assert.False(queries.IsRunning);
    }

    [Fact]
    public async Task The_last_success_is_shown()
    {
        await AddAsync(SyncStatus.Succeeded, Evening.AddDays(-1));
        await AddAsync(SyncStatus.Succeeded, Evening);

        Assert.Equal(new SyncState(null, Evening, null), await queries.LoadAsync(Ct));
    }

    [Fact]
    public async Task A_failure_after_the_last_success_is_shown_with_it()
    {
        await AddAsync(SyncStatus.Succeeded, Evening.AddDays(-1));
        await AddAsync(SyncStatus.Failed, Evening, "Garmin said no");

        Assert.Equal(new SyncState(null, Evening.AddDays(-1), new SyncFailure(Evening, "Garmin said no")), await queries.LoadAsync(Ct));
    }

    [Fact]
    public async Task A_later_success_clears_the_failure()
    {
        await AddAsync(SyncStatus.Failed, Evening.AddDays(-1), "Garmin said no");
        await AddAsync(SyncStatus.Succeeded, Evening);

        Assert.Null((await queries.LoadAsync(Ct)).LastFailure);
    }

    [Fact]
    public async Task A_failure_without_an_error_still_says_so()
    {
        await AddAsync(SyncStatus.Failed, Evening);

        Assert.Equal(new SyncFailure(Evening, "Unknown error"), (await queries.LoadAsync(Ct)).LastFailure);
    }

    [Fact]
    public async Task Starting_runs_a_manual_sync_and_shows_its_progress()
    {
        await AddAsync(SyncStatus.Succeeded, Evening);

        var started = await queries.StartAsync(Ct);

        Assert.True(queries.IsRunning);
        Assert.Equal(new SyncState(new SyncProgress(null, 0, null, 0), Evening, null), started);
        await using var context = await db.CreateDbContextAsync(Ct);
        var sync = await context.SyncRuns.SingleAsync(s => s.Status == SyncStatus.Running, Ct);
        Assert.Equal(SyncTrigger.Manual, sync.Trigger);

        sync.RunsTotal = 120;
        sync.RunsDownloaded = 34;
        await context.SaveChangesAsync(Ct);
        Assert.Equal(new SyncProgress(120, 34, null, 0), (await queries.LoadAsync(Ct)).Running);
    }

    [Fact]
    public async Task Starting_while_a_sync_runs_joins_it()
    {
        await queries.StartAsync(Ct);
        await queries.StartAsync(Ct);

        await using var context = await db.CreateDbContextAsync(Ct);
        Assert.Equal(1, await context.SyncRuns.CountAsync(Ct));
    }

    [Fact]
    public async Task A_finished_sync_is_no_longer_running_even_before_the_coordinator_knows()
    {
        await queries.StartAsync(Ct);
        await using (var context = await db.CreateDbContextAsync(Ct))
        {
            await context.SyncRuns.ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, SyncStatus.Succeeded)
                .SetProperty(x => x.FinishedAtUtc, Evening), Ct);
        }

        Assert.Equal(new SyncState(null, Evening, null), await queries.LoadAsync(Ct));
    }
}
