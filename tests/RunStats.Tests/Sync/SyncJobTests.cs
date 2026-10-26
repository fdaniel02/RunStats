using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using RunStats.Data;
using RunStats.Garmin;
using RunStats.Processing;
using RunStats.Sync;
using RunStats.Tests.Data;
using static RunStats.Tests.Sync.FakeGarminGateway;

namespace RunStats.Tests.Sync;

[Collection("SqlServer")]
public sealed class SyncJobTests(SqlServerFixture sqlServer) : IAsyncLifetime
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    readonly FakeGarminGateway garmin = new();
    readonly FixedTimeProvider time = new(new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero));
    readonly FakeLogger<SyncJob> logger = new();
    readonly ServiceProvider services = new ServiceCollection().AddMetrics().BuildServiceProvider();
    PooledDbContextFactory<AppDbContext> db = null!;
    SyncMetrics metrics = null!;

    public async ValueTask InitializeAsync()
    {
        db = await MigratedDatabase.CreateAsync(sqlServer);
        metrics = new SyncMetrics(services.GetRequiredService<IMeterFactory>());
    }

    public async ValueTask DisposeAsync() => await services.DisposeAsync();

    SyncJob Job(int pageSize = 100) => new(db, garmin, new RunProcessing(db, garmin), metrics, time, logger) { PageSize = pageSize };

    async Task<SyncRun> SyncAsync(SyncJob? job = null, SyncTrigger trigger = SyncTrigger.Schedule, CancellationToken? cancellationToken = null)
    {
        int id;
        await using (var context = await db.CreateDbContextAsync(Ct))
        {
            var sync = new SyncRun { Trigger = trigger, Status = SyncStatus.Running, StartedAtUtc = time.GetUtcNow().UtcDateTime };
            context.SyncRuns.Add(sync);
            await context.SaveChangesAsync(Ct);
            id = sync.Id;
        }
        await (job ?? Job()).RunAsync(id, cancellationToken ?? Ct);
        await using var read = await db.CreateDbContextAsync(Ct);
        return await read.SyncRuns.AsNoTracking().SingleAsync(s => s.Id == id, Ct);
    }

    async Task<List<Run>> StoredRunsAsync()
    {
        await using var context = await db.CreateDbContextAsync(Ct);
        return await context.Runs.Include(r => r.RawData).AsNoTracking().OrderBy(r => r.Id).ToListAsync(Ct);
    }

    [Fact]
    public async Task First_sync_stores_every_run_with_its_raw_data()
    {
        garmin.Activities.AddRange([
            Activity(3, name: "Race day", race: true, distance: 10012.5, duration: 2700.25),
            Activity(2, "treadmill_running", 18, 1),
            Activity(4, "road_biking", 10, 2),
            Activity(1, "obstacle_run", 154, 1, name: new string('x', 250))
        ]);

        var sync = await SyncAsync();

        Assert.Equal((SyncStatus.Succeeded, 3, 3, 3, 0, 0), (sync.Status, sync.RunsTotal, sync.RunsDownloaded, sync.RunsAdded, sync.RunsUpdated, sync.RunsDeleted));
        Assert.Equal(time.GetUtcNow().UtcDateTime, sync.FinishedAtUtc);
        Assert.Null(sync.Error);
        var runs = await StoredRunsAsync();
        Assert.Equal([1L, 2L, 3L], runs.Select(r => r.Id));
        var race = runs[2];
        Assert.Equal(("Race day", "running", 1, 17, true), (race.Name, race.TypeKey, race.TypeId, race.ParentTypeId, race.IsRace));
        Assert.Equal(new DateTime(2026, 10, 25, 9, 30, 0, DateTimeKind.Utc), race.StartUtc);
        Assert.Equal(new DateTime(2026, 10, 25, 10, 30, 0), race.StartLocal);
        Assert.Equal((10012.5, 2700.25, RunProcessor.Version), (race.DistanceMeters, race.DurationSeconds, race.ProcessorVersion));
        Assert.Equal(200, runs[0].Name.Length);
        var raw = race.RawData!;
        Assert.Equal(garmin.Activities[0].Json, raw.ListJson);
        Assert.Equal((DetailJson(3), SplitsJson(3), HeartRateZonesJson(3), WeatherJson(3)), (raw.DetailJson, raw.SplitsJson, raw.HeartRateZonesJson, raw.WeatherJson));
        Assert.Equal(FitFile(3), raw.FitFile);
        Assert.Equal(time.GetUtcNow().UtcDateTime, raw.DownloadedAtUtc);
        Assert.DoesNotContain(garmin.Calls, c => c.EndsWith(" 4", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Missing_fit_file_and_weather_are_stored_as_null()
    {
        garmin.Activities.Add(Activity(1, "treadmill_running", 18, 1));
        garmin.WithoutFitFile.Add(1);
        garmin.WithoutWeather.Add(1);

        await SyncAsync();

        var raw = (await StoredRunsAsync()).Single().RawData!;
        Assert.Null(raw.FitFile);
        Assert.Null(raw.WeatherJson);
    }

    [Theory]
    [InlineData(5, new[] { "list 0", "list 2", "list 4" })]
    [InlineData(4, new[] { "list 0", "list 2", "list 4" })]
    [InlineData(1, new[] { "list 0" })]
    public async Task The_whole_list_is_read_page_by_page(int count, string[] listCalls)
    {
        garmin.Activities.AddRange(Enumerable.Range(1, count).Select(i => Activity(i)));

        var sync = await SyncAsync(Job(pageSize: 2));

        Assert.Equal(count, sync.RunsAdded);
        Assert.Equal(listCalls, garmin.Calls.Where(c => c.StartsWith("list", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Sync_without_changes_downloads_nothing()
    {
        garmin.Activities.AddRange([Activity(1), Activity(2)]);
        await SyncAsync();
        garmin.Calls.Clear();

        var sync = await SyncAsync();

        Assert.Equal((SyncStatus.Succeeded, 0, 0, 0, 0), (sync.Status, sync.RunsTotal, sync.RunsAdded, sync.RunsUpdated, sync.RunsDeleted));
        Assert.Empty(garmin.DownloadCalls);
    }

    [Fact]
    public async Task Run_changed_in_Garmin_is_downloaded_again()
    {
        garmin.Activities.AddRange([Activity(1), Activity(2)]);
        await SyncAsync();
        await using (var context = await db.CreateDbContextAsync(Ct))
            await context.Runs.ExecuteUpdateAsync(r => r.SetProperty(x => x.ProcessorVersion, 1), Ct);
        garmin.Activities[1] = Activity(2, name: "Renamed", race: true);
        garmin.Calls.Clear();
        time.UtcNow = time.UtcNow.AddHours(3);

        var sync = await SyncAsync();

        Assert.Equal((1, 0, 1), (sync.RunsTotal, sync.RunsAdded, sync.RunsUpdated));
        Assert.Equal(["detail 2", "splits 2", "hr-zones 2", "weather 2", "fit 2", "decode 2"], garmin.DownloadCalls);
        var renamed = (await StoredRunsAsync())[1];
        Assert.Equal(("Renamed", true, RunProcessor.Version), (renamed.Name, renamed.IsRace, renamed.ProcessorVersion));
        Assert.Equal(garmin.Activities[1].Json, renamed.RawData!.ListJson);
        Assert.Equal(time.GetUtcNow().UtcDateTime, renamed.RawData.DownloadedAtUtc);
    }

    [Fact]
    public async Task Run_deleted_in_Garmin_is_deleted_with_its_data()
    {
        garmin.Activities.AddRange([Activity(1), Activity(2)]);
        await SyncAsync();
        garmin.Activities.RemoveAt(0);

        var sync = await SyncAsync();

        Assert.Equal(1, sync.RunsDeleted);
        Assert.Equal([2L], (await StoredRunsAsync()).Select(r => r.Id));
        await using var context = await db.CreateDbContextAsync(Ct);
        Assert.Equal(1, await context.RunRawData.CountAsync(Ct));
    }

    [Fact]
    public async Task Garmin_unavailable_stops_the_sync_and_keeps_the_runs_stored_so_far()
    {
        garmin.Activities.AddRange([Activity(3), Activity(2), Activity(1)]);
        garmin.Fail = call => call == "detail 1" ? new GarminUnavailableException("Garmin rate limit reached", TimeSpan.FromSeconds(300)) : null;

        var sync = await SyncAsync();

        Assert.Equal(SyncStatus.Failed, sync.Status);
        Assert.Equal("Garmin is unavailable: Garmin rate limit reached (retry after 300 s)", sync.Error);
        Assert.Equal((3, 2, 2), (sync.RunsTotal, sync.RunsDownloaded, sync.RunsAdded));
        Assert.Equal([2L, 3L], (await StoredRunsAsync()).Select(r => r.Id));
        Assert.Equal(time.GetUtcNow().UtcDateTime, sync.FinishedAtUtc);
        var log = Assert.Single(logger.Collector.GetSnapshot(), r => r.Id.Id == 13);
        Assert.Equal(LogLevel.Warning, log.Level);
    }

    [Fact]
    public async Task Next_sync_continues_where_an_interrupted_one_stopped()
    {
        garmin.Activities.AddRange([Activity(3), Activity(2), Activity(1)]);
        garmin.Fail = call => call == "detail 1" ? new GarminUnavailableException("Garmin rate limit reached", null) : null;
        await SyncAsync();
        garmin.Fail = _ => null;
        garmin.Calls.Clear();

        var sync = await SyncAsync();

        // The first sync stopped before processing, so the second processes all three.
        Assert.Equal((SyncStatus.Succeeded, 1, 3), (sync.Status, sync.RunsAdded, sync.RunsProcessed));
        Assert.All(garmin.DownloadCalls.Where(c => !c.StartsWith("decode", StringComparison.Ordinal)), c => Assert.EndsWith(" 1", c));
        Assert.Equal(3, (await StoredRunsAsync()).Count);
    }

    [Fact]
    public async Task Failed_list_read_deletes_nothing()
    {
        garmin.Activities.AddRange([Activity(1), Activity(2), Activity(3)]);
        await SyncAsync(Job(pageSize: 2));
        garmin.Activities.RemoveAt(0);
        garmin.Fail = call => call == "list 2" ? new HttpRequestException("Bad gateway", null, HttpStatusCode.BadGateway) : null;

        var sync = await SyncAsync(Job(pageSize: 2));

        Assert.Equal(SyncStatus.Failed, sync.Status);
        Assert.Equal("HttpRequestException: Bad gateway", sync.Error);
        Assert.Equal(3, (await StoredRunsAsync()).Count);
        var log = Assert.Single(logger.Collector.GetSnapshot(), r => r.Id.Id == 14);
        Assert.Equal(LogLevel.Error, log.Level);
        Assert.IsType<HttpRequestException>(log.Exception);
    }

    [Fact]
    public async Task Long_errors_are_shortened()
    {
        garmin.Activities.Add(Activity(1));
        garmin.Fail = _ => new InvalidOperationException(new string('e', 3000));

        var sync = await SyncAsync();

        Assert.Equal(2000, sync.Error!.Length);
    }

    [Fact]
    public async Task Stopping_the_app_marks_the_sync_as_interrupted()
    {
        garmin.Activities.AddRange([Activity(2), Activity(1)]);
        using var stopping = new CancellationTokenSource();
        garmin.Fail = call =>
        {
            if (call != "detail 1")
                return null;
            stopping.Cancel();
            return new OperationCanceledException(stopping.Token);
        };

        var sync = await SyncAsync(cancellationToken: stopping.Token);

        Assert.Equal((SyncStatus.Failed, "Interrupted: the app is stopping", 1), (sync.Status, sync.Error, sync.RunsAdded));
    }

    [Fact]
    public async Task Downloaded_runs_are_processed_and_failures_counted()
    {
        garmin.Activities.AddRange([Activity(3), Activity(2), Activity(1)]);
        garmin.Fail = call => call == "decode 2" ? new InvalidFitFileException("Not a valid FIT file") : null;

        var sync = await SyncAsync();

        Assert.Equal((SyncStatus.Succeeded, 3, 2, 1), (sync.Status, sync.RunsToProcess, sync.RunsProcessed, sync.RunsFailedToProcess));
        Assert.Equal(["decode 3", "decode 2", "decode 1"], garmin.Calls.Where(c => c.StartsWith("decode", StringComparison.Ordinal)));
        var log = Assert.Single(logger.Collector.GetSnapshot(), r => r.Id.Id == 17);
        Assert.Equal((LogLevel.Warning, "Run 2 couldn't be processed: InvalidFitFileException: Not a valid FIT file"), (log.Level, log.Message));
        var summary = Assert.Single(logger.Collector.GetSnapshot(), r => r.Id.Id == 18);
        Assert.Equal($"Sync {sync.Id}: 2 runs processed, 1 failed", summary.Message);
        var runs = await StoredRunsAsync();
        Assert.Equal([null, "InvalidFitFileException: Not a valid FIT file", null], runs.Select(r => r.ProcessingError));
    }

    [Fact]
    public async Task Gateway_down_while_processing_fails_the_sync_and_the_next_one_processes()
    {
        garmin.Activities.AddRange([Activity(2), Activity(1)]);
        garmin.Fail = call => call == "decode 1" ? new HttpRequestException("Connection refused") : null;
        var failed = await SyncAsync();
        garmin.Fail = _ => null;

        var next = await SyncAsync();

        Assert.Equal((SyncStatus.Failed, 2, 2, 1), (failed.Status, failed.RunsAdded, failed.RunsToProcess, failed.RunsProcessed));
        Assert.Equal("HttpRequestException: Connection refused", failed.Error);
        Assert.Equal((SyncStatus.Succeeded, 0, 1, 1), (next.Status, next.RunsAdded, next.RunsToProcess, next.RunsProcessed));
    }

    [Fact]
    public async Task Sync_outcomes_and_changes_are_measured()
    {
        var meterFactory = services.GetRequiredService<IMeterFactory>();
        using var syncs = new MetricCollector<long>(meterFactory, Telemetry.Name, "runstats.syncs");
        using var duration = new MetricCollector<double>(meterFactory, Telemetry.Name, "runstats.sync.duration");
        using var runs = new MetricCollector<long>(meterFactory, Telemetry.Name, "runstats.sync.runs");
        using var lastSuccess = new MetricCollector<long>(meterFactory, Telemetry.Name, "runstats.sync.last_success");
        garmin.Activities.AddRange([Activity(1), Activity(2)]);
        await SyncAsync(trigger: SyncTrigger.Manual);
        garmin.Activities.RemoveAt(0);
        garmin.Activities[0] = Activity(2, name: "Renamed");
        garmin.Fail = call => call == "detail 2" ? new GarminUnavailableException("Garmin login failed", null) : null;

        await SyncAsync();

        Assert.Collection(syncs.GetMeasurementSnapshot(),
            m => Assert.Equal(("manual", "succeeded"), (m.Tags["runstats.sync.trigger"], m.Tags["runstats.sync.outcome"])),
            m => Assert.Equal(("schedule", "failed"), (m.Tags["runstats.sync.trigger"], m.Tags["runstats.sync.outcome"])));
        Assert.Equal(2, duration.GetMeasurementSnapshot().Count);
        Assert.Equal([(2L, "added"), (2L, "processed")], runs.GetMeasurementSnapshot().Select(m => (m.Value, m.Tags["runstats.sync.change"])));
        lastSuccess.RecordObservableInstruments();
        Assert.Equal(time.GetUtcNow().ToUnixTimeSeconds(), lastSuccess.LastMeasurement!.Value);
    }
}
