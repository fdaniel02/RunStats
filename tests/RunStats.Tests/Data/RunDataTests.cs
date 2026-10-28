using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using RunStats.Data;

namespace RunStats.Tests.Data;

[Collection("SqlServer")]
public sealed class RunDataTests(SqlServerFixture sqlServer) : IAsyncLifetime
{
    readonly PooledDbContextFactory<AppDbContext> factory =
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(sqlServer.ConnectionString(SqlServerFixture.NewDatabaseName())).Options);

    static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await using var db = await factory.CreateDbContextAsync(Ct);
        await db.Database.MigrateAsync(Ct);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    static Run TreadmillRun(long id) => new()
    {
        Id = id,
        Name = "Treadmill",
        TypeKey = "treadmill_running",
        TypeId = 18,
        ParentTypeId = 1,
        StartUtc = new DateTime(2026, 10, 20, 17, 0, 0, DateTimeKind.Utc),
        StartLocal = new DateTime(2026, 10, 20, 19, 0, 0),
        DistanceMeters = 5000,
        DurationSeconds = 1800
    };

    static Run FullRun(long id) => new()
    {
        Id = id,
        Name = "Lunch Run with ümlauts",
        TypeKey = "running",
        TypeId = 1,
        ParentTypeId = 17,
        IsRace = true,
        StartUtc = new DateTime(2026, 10, 25, 9, 30, 0, DateTimeKind.Utc),
        StartLocal = new DateTime(2026, 10, 25, 10, 30, 0),
        DistanceMeters = 10012.5,
        DurationSeconds = 2700.25,
        MovingDurationSeconds = 2690,
        ElapsedDurationSeconds = 2750,
        AverageSpeedMps = 3.708,
        AverageMovingSpeedMps = 3.72,
        GradeAdjustedSpeedMps = 3.75,
        MaxSpeedMps = 5.1,
        MinHeartRate = 95,
        AverageHeartRate = 162,
        MaxHeartRate = 181,
        AverageCadenceSpm = 172.5,
        MaxCadenceSpm = 190,
        StrideLengthCm = 128.4,
        GroundContactTimeMs = 241.2,
        VerticalOscillationCm = 8.9,
        VerticalRatioPercent = 6.9,
        GroundContactBalanceLeftPercent = 50.4,
        AveragePowerWatts = 310,
        MaxPowerWatts = 520,
        NormalizedPowerWatts = 318,
        ElevationGainMeters = 42,
        ElevationLossMeters = 40,
        MinElevationMeters = -2.4,
        MaxElevationMeters = 18.6,
        AerobicTrainingEffect = 3.4,
        AnaerobicTrainingEffect = 1.2,
        TrainingLoad = 145.7,
        Vo2Max = 52,
        Calories = 720,
        Steps = 7750,
        AverageTemperatureCelsius = -3.5,
        PaceVariationPercent = 3.24,
        StartLatitude = 52.37,
        StartLongitude = -4.89,
        EndLatitude = 52.38,
        EndLongitude = -4.9,
        ProcessorVersion = 1,
        RawData = new RunRawData
        {
            ListJson = """{"activityId":1}""",
            DetailJson = """{"summaryDTO":{}}""",
            SplitsJson = """{"lapDTOs":[]}""",
            HeartRateZonesJson = "[]",
            WeatherJson = null,
            FitFile = [0x0E, 0x10, 0x2E, 0x46, 0x49, 0x54],
            DownloadedAtUtc = new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc)
        },
        Laps =
        [
            new RunLap { LapIndex = 1, StartUtc = new DateTime(2026, 10, 25, 9, 30, 0, DateTimeKind.Utc), IntensityType = "ACTIVE", DistanceMeters = 1000, DurationSeconds = 270, AverageHeartRate = 150, GroundContactBalanceLeftPercent = 51.45, PaceVariationPercent = 2.9 },
            new RunLap { LapIndex = 2, StartUtc = new DateTime(2026, 10, 25, 9, 34, 30, DateTimeKind.Utc), DistanceMeters = 1000, DurationSeconds = 265 }
        ],
        Samples =
        [
            new RunSample
            {
                OffsetSeconds = 0, LatitudeSemicircles = 624_769_324, LongitudeSemicircles = -58_342_310, DistanceMeters = 0, SpeedMps = 0,
                AltitudeMeters = -1.6f, HeartRate = 255, CadenceSpm = 171.5f, StepLengthMm = 1284, GroundContactTimeMs = 241.5f,
                VerticalOscillationMm = 89.2f, VerticalRatioPercent = 6.95f, GroundContactBalanceLeftPercent = 50.25f, PowerWatts = 310, TemperatureCelsius = -12
            },
            new RunSample { OffsetSeconds = 1 }
        ],
        PaceBands =
        [
            new RunPaceBand
            {
                PaceSecondsPerKm = 330, Seconds = 1200, FlatSeconds = 1150, CadenceSpm = 173.4f, StepLengthMm = 1035.5f, GroundContactTimeMs = 254.1f,
                VerticalOscillationMm = 86.2f, VerticalRatioPercent = 8.3f, HeartRate = 157.6f, PowerWatts = 301.2f, GroundContactBalanceLeftPercent = 50.3f
            },
            new RunPaceBand { PaceSecondsPerKm = 335, Seconds = 3, FlatSeconds = 0 }
        ],
        Track = new RunTrack
        {
            EncodedPolyline = "_p~iF~ps|U_ulLnnqC_mqNvxq`@",
            PointCount = 3,
            MinLatitude = 52.37,
            MaxLatitude = 52.38,
            MinLongitude = -4.9,
            MaxLongitude = -4.89
        }
    };

    [Fact]
    public async Task Run_with_everything_reads_back_unchanged()
    {
        var saved = FullRun(21_000_000_001);
        await using (var db = await factory.CreateDbContextAsync(Ct))
        {
            db.Runs.Add(saved);
            await db.SaveChangesAsync(Ct);
        }

        await using var read = await factory.CreateDbContextAsync(Ct);
        var run = await read.Runs
            .Include(r => r.RawData).Include(r => r.Laps).Include(r => r.Samples).Include(r => r.PaceBands).Include(r => r.Track)
            .AsNoTracking().AsSplitQuery().SingleAsync(Ct);

        Assert.Equivalent(saved, run, strict: true);
    }

    [Fact]
    public async Task Run_without_optional_data_reads_back_with_nulls()
    {
        await using (var db = await factory.CreateDbContextAsync(Ct))
        {
            db.Runs.Add(TreadmillRun(21_000_000_002));
            await db.SaveChangesAsync(Ct);
        }

        await using var read = await factory.CreateDbContextAsync(Ct);
        var run = await read.Runs.Include(r => r.RawData).Include(r => r.Track).AsNoTracking().SingleAsync(Ct);

        Assert.Null(run.AverageHeartRate);
        Assert.Null(run.StartLatitude);
        Assert.Null(run.RawData);
        Assert.Null(run.Track);
    }

    [Fact]
    public async Task Utc_dates_read_back_as_utc_and_local_dates_as_unspecified()
    {
        await using (var db = await factory.CreateDbContextAsync(Ct))
        {
            db.Runs.Add(FullRun(21_000_000_003));
            db.SyncRuns.Add(new SyncRun { StartedAtUtc = DateTime.UtcNow, FinishedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync(Ct);
        }

        await using var read = await factory.CreateDbContextAsync(Ct);
        var run = await read.Runs.Include(r => r.RawData).Include(r => r.Laps).AsNoTracking().SingleAsync(Ct);
        var sync = await read.SyncRuns.AsNoTracking().SingleAsync(Ct);

        Assert.Equal(DateTimeKind.Utc, run.StartUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, run.RawData!.DownloadedAtUtc.Kind);
        Assert.All(run.Laps, l => Assert.Equal(DateTimeKind.Utc, l.StartUtc.Kind));
        Assert.Equal(DateTimeKind.Utc, sync.StartedAtUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, sync.FinishedAtUtc!.Value.Kind);
        Assert.Equal(DateTimeKind.Unspecified, run.StartLocal.Kind);
    }

    [Fact]
    public async Task Deleting_a_run_deletes_its_data()
    {
        await using (var db = await factory.CreateDbContextAsync(Ct))
        {
            db.Runs.Add(FullRun(21_000_000_004));
            await db.SaveChangesAsync(Ct);
        }

        await using (var db = await factory.CreateDbContextAsync(Ct))
            await db.Runs.Where(r => r.Id == 21_000_000_004).ExecuteDeleteAsync(Ct);

        await using var read = await factory.CreateDbContextAsync(Ct);
        Assert.Equal(0, await read.RunRawData.CountAsync(Ct));
        Assert.Equal(0, await read.RunLaps.CountAsync(Ct));
        Assert.Equal(0, await read.RunSamples.CountAsync(Ct));
        Assert.Equal(0, await read.RunPaceBands.CountAsync(Ct));
        Assert.Equal(0, await read.RunTracks.CountAsync(Ct));
    }

    [Theory]
    [InlineData("INSERT INTO RunLaps (RunId, LapIndex, StartUtc, DistanceMeters, DurationSeconds) VALUES (21000000005, 1, '2026-10-25', 1, 1)")]
    [InlineData("INSERT INTO RunSamples (RunId, OffsetSeconds) VALUES (21000000005, 0)")]
    [InlineData("INSERT INTO RunPaceBands (RunId, PaceSecondsPerKm, Seconds, FlatSeconds) VALUES (21000000005, 330, 1, 1)")]
    public async Task A_lap_sample_or_pace_band_can_only_be_stored_once(string duplicate)
    {
        await using (var db = await factory.CreateDbContextAsync(Ct))
        {
            db.Runs.Add(FullRun(21_000_000_005));
            await db.SaveChangesAsync(Ct);
        }

        await using var write = await factory.CreateDbContextAsync(Ct);
        var error = await Assert.ThrowsAsync<SqlException>(() => write.Database.ExecuteSqlRawAsync(duplicate, Ct));

        Assert.Equal(2627, error.Number);
    }

    [Fact]
    public async Task Laps_and_samples_need_their_run()
    {
        await using var db = await factory.CreateDbContextAsync(Ct);

        var error = await Assert.ThrowsAsync<SqlException>(() =>
            db.Database.ExecuteSqlRawAsync("INSERT INTO RunSamples (RunId, OffsetSeconds) VALUES (1, 0)", Ct));

        Assert.Equal(547, error.Number);
    }

    [Fact]
    public async Task Samples_are_page_compressed()
    {
        await using var db = await factory.CreateDbContextAsync(Ct);

        var compression = await db.Database.SqlQueryRaw<string>(
            "SELECT p.data_compression_desc AS Value FROM sys.partitions p WHERE p.object_id = OBJECT_ID('RunSamples')").ToListAsync(Ct);

        Assert.Equal(["PAGE"], compression);
    }

    [Fact]
    public async Task Sync_runs_store_trigger_and_status_as_text()
    {
        await using (var db = await factory.CreateDbContextAsync(Ct))
        {
            db.SyncRuns.Add(new SyncRun { Trigger = SyncTrigger.Manual, Status = SyncStatus.Succeeded, StartedAtUtc = DateTime.UtcNow, RunsAdded = 3 });
            await db.SaveChangesAsync(Ct);
        }

        await using var read = await factory.CreateDbContextAsync(Ct);
        var stored = await read.Database.SqlQueryRaw<string>("SELECT [Trigger] + '/' + [Status] AS Value FROM SyncRuns").SingleAsync(Ct);
        var sync = await read.SyncRuns.AsNoTracking().SingleAsync(Ct);

        Assert.Equal("Manual/Succeeded", stored);
        Assert.Equal((SyncTrigger.Manual, SyncStatus.Succeeded, 3), (sync.Trigger, sync.Status, sync.RunsAdded));
    }

    [Fact]
    public async Task Personal_records_are_kept_per_type_and_may_point_to_a_missing_run()
    {
        var record = new PersonalRecord
        {
            TypeId = 3,
            Value = 1199.5,
            RunId = 999,
            AchievedAtUtc = new DateTime(2026, 5, 1, 8, 0, 0, DateTimeKind.Utc),
            AchievedAtLocal = new DateTime(2026, 5, 1, 10, 0, 0),
            RawJson = """{"typeId":3}""",
            UpdatedAtUtc = new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc)
        };
        await using (var db = await factory.CreateDbContextAsync(Ct))
        {
            db.PersonalRecords.Add(record);
            await db.SaveChangesAsync(Ct);
        }

        await using var read = await factory.CreateDbContextAsync(Ct);
        Assert.Equivalent(record, await read.PersonalRecords.AsNoTracking().SingleAsync(Ct), strict: true);
        var duplicate = await Assert.ThrowsAsync<SqlException>(() => read.Database.ExecuteSqlRawAsync(
            "INSERT INTO PersonalRecords (TypeId, Value, AchievedAtUtc, AchievedAtLocal, RawJson, UpdatedAtUtc) VALUES (3, 1, '2026-01-01', '2026-01-01', '', '2026-01-01')", Ct));
        Assert.Equal(2627, duplicate.Number);
    }
}
