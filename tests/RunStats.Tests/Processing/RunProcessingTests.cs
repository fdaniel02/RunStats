using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using RunStats.Data;
using RunStats.Garmin;
using RunStats.Processing;
using RunStats.Tests.Data;
using RunStats.Tests.Sync;
using static RunStats.Tests.Sync.FakeGarminGateway;

namespace RunStats.Tests.Processing;

[Collection("SqlServer")]
public sealed class RunProcessingTests(SqlServerFixture sqlServer) : IAsyncLifetime
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    readonly FakeGarminGateway garmin = new();
    PooledDbContextFactory<AppDbContext> db = null!;
    RunProcessing processing = null!;

    public async ValueTask InitializeAsync()
    {
        db = await MigratedDatabase.CreateAsync(sqlServer);
        processing = new RunProcessing(db, garmin);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    const string Splits = """
        {"lapDTOs":[
          {"lapIndex":1,"startTimeGMT":"2026-10-25T09:30:00.0","distance":1000.0,"duration":270.0},
          {"lapIndex":2,"startTimeGMT":"2026-10-25T09:34:30.0","distance":1000.0,"duration":265.0}]}
        """;

    async Task StoreAsync(long id, int processorVersion = 0, string? splits = Splits, bool fit = true, string? detail = null, DateTime? startUtc = null)
    {
        await using var context = await db.CreateDbContextAsync(Ct);
        context.Runs.Add(new Run
        {
            Id = id,
            Name = "Run",
            TypeKey = "running",
            TypeId = 1,
            ParentTypeId = 17,
            StartUtc = startUtc ?? new DateTime(2026, 10, 25, 9, 30, 0, DateTimeKind.Utc),
            DistanceMeters = 5000,
            DurationSeconds = 1500,
            ProcessorVersion = processorVersion,
            RawData = new RunRawData
            {
                ListJson = """{"activityId":1,"vO2MaxValue":51.0}""",
                DetailJson = detail ?? DetailJson(id),
                SplitsJson = splits,
                FitFile = fit ? FitFile(id) : null,
                DownloadedAtUtc = DateTime.UtcNow
            }
        });
        await context.SaveChangesAsync(Ct);
    }

    async Task<Run> ReadAsync(long id)
    {
        await using var context = await db.CreateDbContextAsync(Ct);
        return await context.Runs.Include(r => r.Laps).Include(r => r.Samples).Include(r => r.PaceBands).Include(r => r.Track)
            .AsNoTracking().AsSplitQuery().SingleAsync(r => r.Id == id, Ct);
    }

    [Fact]
    public async Task Pending_runs_are_the_unprocessed_ones_newest_first()
    {
        await StoreAsync(1, startUtc: new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
        await StoreAsync(2, startUtc: new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc));
        await StoreAsync(3, processorVersion: RunProcessor.Version, startUtc: new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal([2L, 1L], await processing.PendingAsync(Ct));
    }

    [Fact]
    public async Task Processing_fills_stats_laps_samples_pace_bands_and_track()
    {
        await StoreAsync(1);
        garmin.FitSamples[1] = StraightLine(60);

        Assert.Null(await processing.ProcessAsync(1, Ct));

        var run = await ReadAsync(1);
        Assert.Equal((RunProcessor.Version, (string?)null), (run.ProcessorVersion, run.ProcessingError));
        Assert.Equal(((short)150, 170.0, 51.0), (run.AverageHeartRate!.Value, run.AverageCadenceSpm!.Value, run.Vo2Max!.Value));
        Assert.Equal([1, 2], run.Laps.Select(l => l.LapIndex).Order());
        Assert.Equal(60, run.Samples.Count);
        Assert.Equal((byte)199, run.Samples.Single(s => s.OffsetSeconds == 59).HeartRate);
        Assert.Equal(0, run.PaceVariationPercent!.Value, 6);
        // 3 m/s is 5:33/km.
        var band = Assert.Single(run.PaceBands);
        Assert.Equal(((short)330, 60, 60, 170f), (band.PaceSecondsPerKm, band.Seconds, band.FlatSeconds, band.CadenceSpm!.Value));
        Assert.Equal(2, run.Track!.PointCount);
    }

    [Fact]
    public async Task Processing_again_replaces_the_laps_samples_pace_bands_and_track()
    {
        await StoreAsync(1);
        await processing.ProcessAsync(1, Ct);
        garmin.FitSamples[1] = StraightLine(2);

        await processing.ProcessAsync(1, Ct);

        var run = await ReadAsync(1);
        Assert.Equal(2, run.Laps.Count);
        Assert.Equal(2, run.Samples.Count);
        Assert.Equal(2, Assert.Single(run.PaceBands).Seconds);
        Assert.NotNull(run.Track);
    }

    [Fact]
    public async Task Run_without_fit_file_or_splits_gets_its_stats_only()
    {
        await StoreAsync(1, splits: null, fit: false);

        Assert.Null(await processing.ProcessAsync(1, Ct));

        var run = await ReadAsync(1);
        Assert.Equal((short)150, run.AverageHeartRate);
        Assert.Empty(run.Laps);
        Assert.Empty(run.Samples);
        Assert.Null(run.Track);
        Assert.DoesNotContain("decode 1", garmin.Calls);
    }

    [Fact]
    public async Task Treadmill_run_has_samples_but_no_track()
    {
        await StoreAsync(1);
        garmin.FitSamples[1] = [.. StraightLine(5).Select(s => s with { LatitudeSemicircles = null, LongitudeSemicircles = null })];

        await processing.ProcessAsync(1, Ct);

        var run = await ReadAsync(1);
        Assert.Equal(5, run.Samples.Count);
        Assert.Null(run.Track);
    }

    [Fact]
    public async Task Broken_fit_file_is_recorded_on_the_run_and_its_old_data_removed()
    {
        await StoreAsync(1);
        await processing.ProcessAsync(1, Ct);
        await using (var context = await db.CreateDbContextAsync(Ct))
            await context.Runs.ExecuteUpdateAsync(r => r.SetProperty(x => x.ProcessorVersion, 0), Ct);
        garmin.Fail = call => call == "decode 1" ? new InvalidFitFileException("Not a valid FIT file: CRC mismatch") : null;

        var error = await processing.ProcessAsync(1, Ct);

        Assert.Equal("InvalidFitFileException: Not a valid FIT file: CRC mismatch", error);
        var run = await ReadAsync(1);
        Assert.Equal((RunProcessor.Version, error), (run.ProcessorVersion, run.ProcessingError));
        Assert.Empty(run.Laps);
        Assert.Empty(run.Samples);
        Assert.Empty(run.PaceBands);
        Assert.Null(run.Track);
    }

    [Fact]
    public async Task Broken_summary_keeps_the_runs_stats()
    {
        await StoreAsync(1, detail: """{"activityId":1}""");

        var error = await processing.ProcessAsync(1, Ct);

        Assert.Equal("FormatException: The activity detail has no summary.", error);
        var run = await ReadAsync(1);
        Assert.Equal((5000.0, (short?)null), (run.DistanceMeters, run.AverageHeartRate));
    }

    [Fact]
    public async Task Long_errors_are_shortened()
    {
        await StoreAsync(1);
        garmin.Fail = call => call == "decode 1" ? new InvalidFitFileException(new string('e', 2000)) : null;

        var error = await processing.ProcessAsync(1, Ct);

        Assert.Equal(1000, error!.Length);
        Assert.Equal(error, (await ReadAsync(1)).ProcessingError);
    }

    [Fact]
    public async Task Run_without_raw_data_is_recorded_as_an_error()
    {
        await using (var context = await db.CreateDbContextAsync(Ct))
        {
            context.Runs.Add(new Run { Id = 1, Name = "Run", TypeKey = "running", TypeId = 1, StartUtc = DateTime.UtcNow });
            await context.SaveChangesAsync(Ct);
        }

        Assert.Equal("FormatException: The run has no raw data.", await processing.ProcessAsync(1, Ct));
    }

    [Fact]
    public async Task Gateway_problems_are_thrown_and_leave_the_run_unprocessed()
    {
        await StoreAsync(1);
        garmin.Fail = call => call == "decode 1" ? new HttpRequestException("Connection refused", null, HttpStatusCode.ServiceUnavailable) : null;

        await Assert.ThrowsAsync<HttpRequestException>(() => processing.ProcessAsync(1, Ct));

        var run = await ReadAsync(1);
        Assert.Equal((0, (string?)null), (run.ProcessorVersion, run.ProcessingError));
    }
}
