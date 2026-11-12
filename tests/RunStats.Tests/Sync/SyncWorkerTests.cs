using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using RunStats.Data;
using RunStats.Sync;
using RunStats.Tests.Data;
using static RunStats.Tests.Sync.FakeGarminGateway;

namespace RunStats.Tests.Sync;

[Collection("Postgres")]
public sealed class SyncWorkerTests(PostgresFixture postgres) : IAsyncLifetime
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    readonly FakeGarminGateway garmin = new();
    readonly FixedTimeProvider time = new(new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero));
    readonly FakeLogger<SyncWorker> logger = new();
    readonly FakeLogger<DatabaseSizeMonitor> sizeLogger = new();
    readonly ServiceProvider meters = new ServiceCollection().AddMetrics().BuildServiceProvider();
    PooledDbContextFactory<AppDbContext> db = null!;
    IDbContextFactory<AppDbContext>? jobDb;
    IDbContextFactory<AppDbContext>? sizeDb;

    public async ValueTask InitializeAsync() => db = await MigratedDatabase.CreateAsync(postgres);

    public ValueTask DisposeAsync() => meters.DisposeAsync();

    sealed class UnreachableDatabase : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => throw new NotSupportedException();

        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Database is down");
    }

    async Task<SyncRun> SyncAsync()
    {
        var meterFactory = meters.GetRequiredService<IMeterFactory>();
        var metrics = new SyncMetrics(meterFactory);
        await using var services = new ServiceCollection()
            .AddTransient(_ => TestSyncJob.Create(jobDb ?? db, garmin, new FakeNotionRaces(), time, metrics, NullLogger<SyncJob>.Instance))
            .BuildServiceProvider();
        using var coordinator = new SyncCoordinator(db, metrics, time, NullLogger<SyncCoordinator>.Instance);
        using var sizeMonitor = new DatabaseSizeMonitor(sizeDb ?? db, new DatabaseSizeMetrics(meterFactory), sizeLogger);
        using var worker = new SyncWorker(coordinator, services.GetRequiredService<IServiceScopeFactory>(), sizeMonitor, logger);

        await worker.StartAsync(Ct);
        var request = await coordinator.RequestAsync(SyncTrigger.Schedule, Ct);
        for (var attempt = 0; coordinator.RunningSyncId is not null; attempt++)
        {
            if (attempt == 200)
                throw new TimeoutException("The sync didn't finish.");
            await Task.Delay(50, Ct);
        }
        await worker.StopAsync(Ct);

        await using var read = await db.CreateDbContextAsync(Ct);
        return await read.SyncRuns.AsNoTracking().SingleAsync(s => s.Id == request.SyncId, Ct);
    }

    long? ReportedRunsTableSize()
    {
        using var tables = new MetricCollector<long>(meters.GetRequiredService<IMeterFactory>(), Telemetry.Name, "runstats.db.table.size");
        tables.RecordObservableInstruments();
        return tables.GetMeasurementSnapshot().Where(m => (string?)m.Tags["runstats.db.table"] == "Runs").Select(m => (long?)m.Value).LastOrDefault();
    }

    [Fact]
    public async Task A_sync_reports_the_database_size_afterwards()
    {
        Assert.Null(ReportedRunsTableSize());
        garmin.Activities.Add(Activity(1));

        var sync = await SyncAsync();

        Assert.Equal(SyncStatus.Succeeded, sync.Status);
        Assert.True(ReportedRunsTableSize() > 0);
    }

    [Fact]
    public async Task A_failed_sync_reports_the_database_size_too()
    {
        garmin.Fail = _ => new InvalidOperationException("Garmin is down");

        var sync = await SyncAsync();

        Assert.Equal(SyncStatus.Failed, sync.Status);
        Assert.NotNull(ReportedRunsTableSize());
    }

    [Fact]
    public async Task A_crashed_sync_reports_the_database_size_too()
    {
        jobDb = new UnreachableDatabase();

        var sync = await SyncAsync();

        Assert.Equal(SyncStatus.Running, sync.Status);
        Assert.Equal(15, logger.LatestRecord.Id.Id);
        Assert.NotNull(ReportedRunsTableSize());
    }

    [Fact]
    public async Task A_failed_size_read_doesnt_fail_the_sync()
    {
        garmin.Activities.Add(Activity(1));
        sizeDb = new UnreachableDatabase();

        var sync = await SyncAsync();

        Assert.Equal((SyncStatus.Succeeded, 1), (sync.Status, sync.RunsAdded));
        Assert.Equal("The database size couldn't be read", sizeLogger.LatestRecord.Message);
        Assert.Null(ReportedRunsTableSize());
    }
}
