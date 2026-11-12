using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Options;
using RunStats.Data;
using RunStats.Features.Trends;
using RunStats.Tests.Data;

namespace RunStats.Tests.Features;

[Collection("Postgres")]
public sealed class EfficiencyQueriesTests(PostgresFixture postgres) : IAsyncLifetime
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Tuesday 27 October 2026, 10:00 in Amsterdam.
    readonly FixedTimeProvider time = new(new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero));
    PooledDbContextFactory<AppDbContext> db = null!;
    long nextId = 1;

    public async ValueTask InitializeAsync() => db = await MigratedDatabase.CreateAsync(postgres);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    EfficiencyQueries Queries(EfficiencySettings? settings = null) =>
        new(db, Options.Create(settings ?? new EfficiencySettings()), new Clock(TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam"), time));

    // Each band: its heart rate, minutes and pace in seconds per km.
    async Task AddAsync(DateTime startLocal, string typeKey = "running", double? temperature = null, params (short HeartRate, int Minutes, double Pace)[] bands)
    {
        var id = nextId++;
        await using var context = await db.CreateDbContextAsync(Ct);
        context.Runs.Add(new Run
        {
            Id = id,
            Name = "Run",
            TypeKey = typeKey,
            TypeId = 1,
            ParentTypeId = 17,
            StartLocal = startLocal,
            StartUtc = DateTime.SpecifyKind(startLocal.AddHours(-1), DateTimeKind.Utc),
            DistanceMeters = 10_000,
            DurationSeconds = 3600,
            WeatherTemperatureCelsius = temperature,
            HeartRateBands = [.. bands.Select(b => new RunHeartRateBand { HeartRate = b.HeartRate, Seconds = b.Minutes * 60, DistanceMeters = b.Minutes * 60 * 1000 / b.Pace })]
        });
        await context.SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task Without_runs_in_the_band_there_are_no_months()
    {
        await AddAsync(new DateTime(2026, 10, 1, 9, 0, 0), bands: (170, 40, 300));

        var data = await Queries().LoadAsync(Ct);

        Assert.Equal((150, 160, 30.0), (data.HeartRateFrom, data.HeartRateTo, data.MonthMinMinutes));
        Assert.Empty(data.Months);
    }

    [Fact]
    public async Task A_months_pace_is_its_time_in_the_band_over_its_distance()
    {
        // 20 min at 6:00 and 20 min at 5:00 /km: 40 min over 7.33 km is 5:27 /km.
        await AddAsync(new DateTime(2026, 9, 3, 9, 0, 0), bands: [(150, 20, 360), (165, 30, 300)]);
        await AddAsync(new DateTime(2026, 9, 20, 9, 0, 0), bands: (155, 20, 300));

        var september = (await Queries().LoadAsync(Ct)).Months[0];

        Assert.Equal((new DateOnly(2026, 9, 1), 2400.0, 2), (september.Month, september.Seconds, september.Runs));
        Assert.Equal(2400 / (1200 / 360.0 + 1200 / 300.0), september.PaceSecondsPerKm!.Value, 6);
    }

    [Fact]
    public async Task Months_run_to_this_one_and_too_few_minutes_give_no_pace()
    {
        await AddAsync(new DateTime(2026, 7, 31, 23, 30, 0), bands: (150, 40, 330));
        await AddAsync(new DateTime(2026, 8, 1, 0, 30, 0), bands: (150, 10, 330));

        var months = (await Queries().LoadAsync(Ct)).Months;

        Assert.Equal([new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1)], months.Select(m => m.Month));
        Assert.Equal(330, months[0].PaceSecondsPerKm!.Value, 6);
        Assert.Equal((600.0, 1, (double?)null), (months[1].Seconds, months[1].Runs, months[1].PaceSecondsPerKm));
        Assert.Equal((0.0, 0), (months[2].Seconds, months[2].Runs));
    }

    [Fact]
    public async Task Only_road_runs_count()
    {
        await AddAsync(new DateTime(2026, 10, 3, 9, 0, 0), typeKey: "trail_running", bands: (150, 40, 400));
        await AddAsync(new DateTime(2026, 10, 4, 9, 0, 0), bands: (150, 40, 330));

        var october = Assert.Single((await Queries().LoadAsync(Ct)).Months);

        Assert.Equal(1, october.Runs);
        Assert.Equal(330, october.PaceSecondsPerKm!.Value, 6);
    }

    [Fact]
    public async Task The_temperature_is_weighted_by_minutes_in_the_band()
    {
        await AddAsync(new DateTime(2026, 10, 3, 9, 0, 0), temperature: 10, bands: (150, 30, 330));
        await AddAsync(new DateTime(2026, 10, 10, 9, 0, 0), temperature: 20, bands: (150, 10, 330));
        await AddAsync(new DateTime(2026, 10, 17, 9, 0, 0), bands: (150, 10, 330));

        var october = Assert.Single((await Queries().LoadAsync(Ct)).Months);

        Assert.Equal(12.5, october.TemperatureCelsius!.Value, 6);
    }

    [Fact]
    public async Task The_band_comes_from_the_settings()
    {
        await AddAsync(new DateTime(2026, 10, 3, 9, 0, 0), bands: [(150, 40, 330), (160, 40, 300)]);

        var data = await Queries(new EfficiencySettings { HeartRateFrom = 160, HeartRateTo = 170, MonthMinMinutes = 45 }).LoadAsync(Ct);

        var october = Assert.Single(data.Months);
        Assert.Equal((2400.0, (double?)null), (october.Seconds, october.PaceSecondsPerKm));
        Assert.Equal((160, 170, 45.0), (data.HeartRateFrom, data.HeartRateTo, data.MonthMinMinutes));
    }
}
