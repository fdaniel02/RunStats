using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RunStats.Data;
using RunStats.Garmin;
using RunStats.Tests.Data;
using RunStats.Tests.Sync;

namespace RunStats.Tests.Features;

[Collection("Postgres")]
public sealed class RunsEndpointTests : IDisposable
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;
    static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    readonly WebApplicationFactory<Program> factory;
    readonly HttpClient client;
    long nextId = 1;

    public RunsEndpointTests(PostgresFixture postgres)
    {
        var connectionString = postgres.ConnectionString(PostgresFixture.NewDatabaseName());
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseEnvironment("Production")
            .UseSetting("ConnectionStrings:Default", connectionString)
            .UseSetting("TimeZone", "Europe/Amsterdam")
            .ConfigureServices(s =>
            {
                s.RemoveAll<IGarminGateway>();
                s.AddSingleton<IGarminGateway>(new FakeGarminGateway());
            }));
        client = factory.CreateClient();
    }

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
    }

    async Task AddAsync(DateTime startUtc, double km, double? calories = null, double? trainingLoad = null, string typeKey = "running")
    {
        await using var db = await factory.Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync(Ct);
        var utc = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc);
        db.Runs.Add(new Run
        {
            Id = nextId++,
            Name = "Run",
            TypeKey = typeKey,
            TypeId = 1,
            ParentTypeId = 17,
            StartUtc = utc,
            StartLocal = TimeZoneInfo.ConvertTimeFromUtc(utc, Amsterdam),
            DistanceMeters = km * 1000,
            DurationSeconds = km * 300,
            Calories = calories,
            TrainingLoad = trainingLoad,
            AverageHeartRate = 150
        });
        await db.SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task Returns_the_runs_of_the_days_asked_for_in_the_apps_time_zone()
    {
        await AddAsync(new DateTime(2026, 11, 9, 22, 30, 0), 5);                // 9 Nov 23:30 in Amsterdam: before the range
        await AddAsync(new DateTime(2026, 11, 9, 23, 30, 0), 10, 650, 120.5);   // 10 Nov 00:30
        await AddAsync(new DateTime(2026, 11, 11, 17, 0, 0), 8, 520, null, "treadmill_running");
        await AddAsync(new DateTime(2026, 11, 11, 23, 0, 0), 6);                // 12 Nov 00:00: after the range

        var runs = await client.GetFromJsonAsync<JsonElement>("/api/runs?from=2026-11-10&to=2026-11-11", Ct);

        Assert.Equal(2, runs.GetArrayLength());
        var first = runs[0];
        Assert.Equal(2, first.GetProperty("id").GetInt64());
        Assert.Equal("2026-11-10", first.GetProperty("date").GetString());
        Assert.Equal(new DateTime(2026, 11, 9, 23, 30, 0, DateTimeKind.Utc), first.GetProperty("startUtc").GetDateTime().ToUniversalTime());
        Assert.Equal(10_000, first.GetProperty("distanceMeters").GetDouble());
        Assert.Equal(3000, first.GetProperty("durationSeconds").GetDouble());
        Assert.Equal(650, first.GetProperty("calories").GetDouble());
        Assert.Equal(120.5, first.GetProperty("trainingLoad").GetDouble());
        Assert.Equal(150, first.GetProperty("averageHeartRate").GetInt32());
        Assert.Equal("running", first.GetProperty("typeKey").GetString());
        var second = runs[1];
        Assert.Equal("2026-11-11", second.GetProperty("date").GetString());
        Assert.Equal("treadmill_running", second.GetProperty("typeKey").GetString());
        Assert.Equal(JsonValueKind.Null, second.GetProperty("trainingLoad").ValueKind);
    }

    [Fact]
    public async Task A_day_without_runs_is_an_empty_list()
    {
        var runs = await client.GetFromJsonAsync<JsonElement>("/api/runs?from=2026-11-10&to=2026-11-10", Ct);

        Assert.Equal(0, runs.GetArrayLength());
    }

    [Theory]
    [InlineData("from=2026-11-11&to=2026-11-10")]
    [InlineData("from=2025-01-01&to=2026-01-02")]
    public async Task A_reversed_or_too_long_range_is_rejected(string query)
    {
        var response = await client.GetAsync($"/api/runs?{query}", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("from=2026-11-10")]
    [InlineData("from=yesterday&to=2026-11-10")]
    public async Task A_missing_or_invalid_date_is_rejected(string query)
    {
        var response = await client.GetAsync($"/api/runs?{query}", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_whole_leap_year_is_allowed()
    {
        await AddAsync(new DateTime(2024, 12, 31, 8, 0, 0), 5);

        var runs = await client.GetFromJsonAsync<JsonElement>("/api/runs?from=2024-01-01&to=2024-12-31", Ct);

        Assert.Equal(1, runs.GetArrayLength());
    }
}
