using System.Diagnostics.Metrics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using RunStats.Data;

namespace RunStats.Tests.Data;

[Collection("SqlServer")]
public sealed class DatabaseSizeTests(SqlServerFixture sqlServer) : IAsyncLifetime
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    readonly ServiceProvider services = new ServiceCollection().AddMetrics().BuildServiceProvider();
    PooledDbContextFactory<AppDbContext> db = null!;

    public async ValueTask InitializeAsync() => db = await MigratedDatabase.CreateAsync(sqlServer);

    public async ValueTask DisposeAsync() => await services.DisposeAsync();

    static async Task<DatabaseSize> ReadAsync(IDbContextFactory<AppDbContext> factory)
    {
        await using var context = await factory.CreateDbContextAsync(Ct);
        return await DatabaseSizeMetrics.ReadAsync(context, Ct);
    }

    async Task AddSamplesAsync(int count)
    {
        await using var context = await db.CreateDbContextAsync(Ct);
        context.Runs.Add(new Run
        {
            Id = 1,
            Name = "Run",
            TypeKey = "running",
            StartUtc = DateTime.UtcNow,
            StartLocal = DateTime.Now,
            DistanceMeters = 5000,
            DurationSeconds = 1800,
            Samples = [.. Enumerable.Range(0, count).Select(i => new RunSample { OffsetSeconds = i, DistanceMeters = i * 3, HeartRate = 150 })]
        });
        await context.SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task Reads_the_data_and_log_files_and_every_table()
    {
        var size = await ReadAsync(db);

        Assert.Equal(["data", "log"], size.Files.Keys.Order());
        Assert.All(size.Files.Values, bytes => Assert.True(bytes > 0));
        Assert.Contains("Runs", size.Tables.Keys);
        Assert.Contains("RunSamples", size.Tables.Keys);
        Assert.Contains("RunRawData", size.Tables.Keys);
        Assert.All(size.Tables.Values, bytes => Assert.Equal(0, bytes % 8192));
    }

    [Fact]
    public async Task A_table_grows_with_its_rows()
    {
        var before = (await ReadAsync(db)).Tables["RunSamples"];

        await AddSamplesAsync(20_000);

        Assert.True((await ReadAsync(db)).Tables["RunSamples"] > before + 100 * 1024);
    }

    [Fact]
    public async Task The_apps_login_sees_the_same_sizes_without_extra_permissions()
    {
        await AddSamplesAsync(5_000);
        await using var context = await db.CreateDbContextAsync(Ct);
        var login = $"app_{Guid.NewGuid():N}"[..20];
        const string password = "Size-check-Passw0rd!";
        // The same roles scripts/setup-cluster-database.sh gives the runstats login. DDL can't take the login's name as a
        // parameter; it's generated above, not input.
#pragma warning disable EF1002
        await context.Database.ExecuteSqlRawAsync($"""
            CREATE LOGIN [{login}] WITH PASSWORD = N'{password}', CHECK_POLICY = OFF;
            CREATE USER [{login}] FOR LOGIN [{login}];
            ALTER ROLE [db_datareader] ADD MEMBER [{login}];
            ALTER ROLE [db_datawriter] ADD MEMBER [{login}];
            ALTER ROLE [db_ddladmin] ADD MEMBER [{login}];
            """, Ct);
#pragma warning restore EF1002
        var connection = new SqlConnectionStringBuilder(sqlServer.ConnectionString(context.Database.GetDbConnection().Database))
        {
            UserID = login,
            Password = password
        };
        var asApp = new PooledDbContextFactory<AppDbContext>(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connection.ConnectionString).Options);

        var seenByApp = await ReadAsync(asApp);
        var seenByAdmin = await ReadAsync(db);

        Assert.Equal(seenByAdmin.Files, seenByApp.Files);
        Assert.Equal(seenByAdmin.Tables, seenByApp.Tables);
        Assert.True(seenByApp.Tables["RunSamples"] > 0);
    }

    [Fact]
    public async Task The_monitor_reads_the_sizes_at_start_and_again_every_quarter_hour()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var metrics = new DatabaseSizeMetrics(services.GetRequiredService<IMeterFactory>());
        using var tables = new MetricCollector<long>(services.GetRequiredService<IMeterFactory>(), Telemetry.Name, "runstats.db.table.size");
        using var monitor = new DatabaseSizeMonitor(db, metrics, time, new FakeLogger<DatabaseSizeMonitor>());
        long Samples()
        {
            tables.RecordObservableInstruments();
            return tables.LastMeasurement is null ? -1 : tables.GetMeasurementSnapshot()
                .Last(m => (string?)m.Tags["runstats.db.table"] == "RunSamples").Value;
        }

        await monitor.StartAsync(Ct);
        await WaitUntilAsync(() => Samples() >= 0);
        var first = Samples();
        await AddSamplesAsync(20_000);

        time.Advance(DatabaseSizeMonitor.Interval - TimeSpan.FromSeconds(1));
        await Task.Delay(200, Ct);
        Assert.Equal(first, Samples());
        time.Advance(TimeSpan.FromSeconds(1));
        await WaitUntilAsync(() => Samples() > first);
        await monitor.StopAsync(Ct);
    }

    sealed class FailingOnce(IDbContextFactory<AppDbContext> inner) : IDbContextFactory<AppDbContext>
    {
        public int Calls { get; private set; }

        public AppDbContext CreateDbContext() => throw new NotSupportedException();

        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            ++Calls == 1 ? throw new InvalidOperationException("Database is down") : inner.CreateDbContextAsync(cancellationToken);
    }

    [Fact]
    public async Task A_failed_read_is_logged_and_tried_again_later()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var logger = new FakeLogger<DatabaseSizeMonitor>();
        var factory = new FailingOnce(db);
        var metrics = new DatabaseSizeMetrics(services.GetRequiredService<IMeterFactory>());
        using var files = new MetricCollector<long>(services.GetRequiredService<IMeterFactory>(), Telemetry.Name, "runstats.db.size");
        using var monitor = new DatabaseSizeMonitor(factory, metrics, time, logger);

        await monitor.StartAsync(Ct);
        await WaitUntilAsync(() => logger.Collector.Count == 1);
        Assert.Equal("The database size couldn't be read", logger.LatestRecord.Message);
        files.RecordObservableInstruments();
        Assert.Empty(files.GetMeasurementSnapshot());

        time.Advance(DatabaseSizeMonitor.Interval);
        await WaitUntilAsync(() =>
        {
            files.RecordObservableInstruments();
            return files.GetMeasurementSnapshot().Count > 0;
        });
        Assert.Equal(2, factory.Calls);
        await monitor.StopAsync(Ct);
    }

    // Like SQL Server: a read cancelled by the stop fails with its own error, not with OperationCanceledException.
    sealed class FailingWhenStopped : IDbContextFactory<AppDbContext>
    {
        public TaskCompletionSource Reading { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public AppDbContext CreateDbContext() => throw new NotSupportedException();

        public async Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            Reading.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken).ContinueWith(_ => { }, TaskScheduler.Default);
            throw new InvalidOperationException("A severe error occurred on the current command. Operation cancelled by user.");
        }
    }

    [Fact]
    public async Task Stopping_during_a_read_is_no_error()
    {
        var logger = new FakeLogger<DatabaseSizeMonitor>();
        var factory = new FailingWhenStopped();
        var metrics = new DatabaseSizeMetrics(services.GetRequiredService<IMeterFactory>());
        using var monitor = new DatabaseSizeMonitor(factory, metrics, new FakeTimeProvider(DateTimeOffset.UtcNow), logger);

        await monitor.StartAsync(Ct);
        await factory.Reading.Task.WaitAsync(Ct);
        await monitor.StopAsync(Ct);

        Assert.True(monitor.ExecuteTask!.IsCompletedSuccessfully);
        Assert.Equal(0, logger.Collector.Count);
    }

    [Fact]
    public void The_gauges_report_nothing_before_the_first_read_and_then_each_file_and_table()
    {
        var metrics = new DatabaseSizeMetrics(services.GetRequiredService<IMeterFactory>());
        using var files = new MetricCollector<long>(services.GetRequiredService<IMeterFactory>(), Telemetry.Name, "runstats.db.size");
        using var tables = new MetricCollector<long>(services.GetRequiredService<IMeterFactory>(), Telemetry.Name, "runstats.db.table.size");

        files.RecordObservableInstruments();
        Assert.Empty(files.GetMeasurementSnapshot());

        metrics.Update(new DatabaseSize(
            new Dictionary<string, long> { ["data"] = 75_497_472, ["log"] = 142_606_336 },
            new Dictionary<string, long> { ["RunSamples"] = 18_661_376 }));
        files.RecordObservableInstruments();
        tables.RecordObservableInstruments();

        Assert.Equal([("data", 75_497_472L), ("log", 142_606_336L)],
            files.GetMeasurementSnapshot().Select(m => ((string)m.Tags["runstats.db.file"]!, m.Value)).Order());
        Assert.Equal([("RunSamples", 18_661_376L)], tables.GetMeasurementSnapshot().Select(m => ((string)m.Tags["runstats.db.table"]!, m.Value)));
        Assert.Equal("By", files.Instrument!.Unit);
    }

    static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
            await Task.Delay(50, Ct);
        Assert.True(condition());
    }
}
