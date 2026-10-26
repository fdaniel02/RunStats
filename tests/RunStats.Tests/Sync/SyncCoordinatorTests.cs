using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using RunStats.Data;
using RunStats.Sync;
using RunStats.Tests.Data;

namespace RunStats.Tests.Sync;

[Collection("SqlServer")]
public sealed class SyncCoordinatorTests(SqlServerFixture sqlServer) : IAsyncLifetime
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    readonly FixedTimeProvider time = new(new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero));
    readonly FakeLogger<SyncCoordinator> logger = new();
    readonly ServiceProvider services = new ServiceCollection().AddMetrics().BuildServiceProvider();
    PooledDbContextFactory<AppDbContext> db = null!;
    SyncCoordinator coordinator = null!;

    public async ValueTask InitializeAsync()
    {
        db = await MigratedDatabase.CreateAsync(sqlServer);
        coordinator = new SyncCoordinator(db, new SyncMetrics(services.GetRequiredService<IMeterFactory>()), time, logger);
    }

    public async ValueTask DisposeAsync()
    {
        coordinator.Dispose();
        await services.DisposeAsync();
    }

    async Task<SyncRun> StoredAsync(int id)
    {
        await using var context = await db.CreateDbContextAsync(Ct);
        return await context.SyncRuns.AsNoTracking().SingleAsync(s => s.Id == id, Ct);
    }

    [Fact]
    public async Task Request_records_a_running_sync_and_queues_it()
    {
        var request = await coordinator.RequestAsync(SyncTrigger.Manual, Ct);

        Assert.True(request.Started);
        Assert.Equal(request.SyncId, coordinator.RunningSyncId);
        Assert.True(coordinator.Queue.TryRead(out var queued));
        Assert.Equal(request.SyncId, queued);
        var sync = await StoredAsync(request.SyncId);
        Assert.Equal((SyncTrigger.Manual, SyncStatus.Running, time.GetUtcNow().UtcDateTime), (sync.Trigger, sync.Status, sync.StartedAtUtc));
    }

    [Fact]
    public async Task Request_while_a_sync_runs_returns_that_sync()
    {
        var first = await coordinator.RequestAsync(SyncTrigger.Schedule, Ct);

        var second = await coordinator.RequestAsync(SyncTrigger.Manual, Ct);

        Assert.Equal(new SyncRequest(first.SyncId, Started: false), second);
        await using var context = await db.CreateDbContextAsync(Ct);
        Assert.Equal(1, await context.SyncRuns.CountAsync(Ct));
    }

    [Fact]
    public async Task Concurrent_requests_start_one_sync()
    {
        var requests = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => coordinator.RequestAsync(SyncTrigger.Manual, Ct)));

        Assert.Single(requests, r => r.Started);
        Assert.Single(requests.Select(r => r.SyncId).Distinct());
    }

    [Fact]
    public async Task A_new_sync_can_start_once_the_running_one_finished()
    {
        var first = await coordinator.RequestAsync(SyncTrigger.Schedule, Ct);
        coordinator.Finished(first.SyncId);

        var second = await coordinator.RequestAsync(SyncTrigger.Schedule, Ct);

        Assert.True(second.Started);
        Assert.NotEqual(first.SyncId, second.SyncId);
    }

    [Fact]
    public async Task Finishing_another_sync_does_not_free_the_running_one()
    {
        var running = await coordinator.RequestAsync(SyncTrigger.Schedule, Ct);

        coordinator.Finished(running.SyncId + 100);

        Assert.Equal(running.SyncId, coordinator.RunningSyncId);
    }

    [Fact]
    public async Task Startup_marks_syncs_left_running_as_interrupted()
    {
        int leftRunning, succeeded;
        await using (var context = await db.CreateDbContextAsync(Ct))
        {
            var running = new SyncRun { Status = SyncStatus.Running, StartedAtUtc = time.GetUtcNow().UtcDateTime.AddHours(-1) };
            var done = new SyncRun { Status = SyncStatus.Succeeded, StartedAtUtc = time.GetUtcNow().UtcDateTime.AddHours(-4) };
            context.SyncRuns.AddRange(running, done);
            await context.SaveChangesAsync(Ct);
            (leftRunning, succeeded) = (running.Id, done.Id);
        }

        await coordinator.InitializeAsync(Ct);

        var interrupted = await StoredAsync(leftRunning);
        Assert.Equal((SyncStatus.Failed, "Interrupted: the app stopped during the sync"), (interrupted.Status, interrupted.Error));
        Assert.Equal(time.GetUtcNow().UtcDateTime, interrupted.FinishedAtUtc);
        Assert.Equal(SyncStatus.Succeeded, (await StoredAsync(succeeded)).Status);
        var log = Assert.Single(logger.Collector.GetSnapshot());
        Assert.Equal((LogLevel.Warning, 16), (log.Level, log.Id.Id));
    }

    [Fact]
    public async Task Startup_without_interrupted_syncs_logs_nothing()
    {
        await coordinator.InitializeAsync(Ct);

        Assert.Empty(logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task Startup_reports_the_last_successful_sync()
    {
        var lastSuccess = new DateTime(2026, 10, 26, 9, 0, 5, DateTimeKind.Utc);
        await using (var context = await db.CreateDbContextAsync(Ct))
        {
            context.SyncRuns.AddRange(
                new SyncRun { Status = SyncStatus.Succeeded, StartedAtUtc = lastSuccess.AddHours(-3), FinishedAtUtc = lastSuccess.AddHours(-3) },
                new SyncRun { Status = SyncStatus.Succeeded, StartedAtUtc = lastSuccess.AddMinutes(-1), FinishedAtUtc = lastSuccess },
                new SyncRun { Status = SyncStatus.Failed, StartedAtUtc = lastSuccess.AddHours(2), FinishedAtUtc = lastSuccess.AddHours(2) });
            await context.SaveChangesAsync(Ct);
        }
        using var gauge = new MetricCollector<long>(services.GetRequiredService<IMeterFactory>(), Telemetry.Name, "runstats.sync.last_success");

        await coordinator.InitializeAsync(Ct);

        gauge.RecordObservableInstruments();
        Assert.Equal(new DateTimeOffset(lastSuccess).ToUnixTimeSeconds(), gauge.LastMeasurement!.Value);
    }

    [Fact]
    public async Task No_last_success_is_reported_before_the_first_successful_sync()
    {
        using var gauge = new MetricCollector<long>(services.GetRequiredService<IMeterFactory>(), Telemetry.Name, "runstats.sync.last_success");

        await coordinator.InitializeAsync(Ct);

        gauge.RecordObservableInstruments();
        Assert.Null(gauge.LastMeasurement);
    }
}
