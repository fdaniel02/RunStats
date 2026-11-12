using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using RunStats.Data;
using RunStats.Processing;
using RunStats.Tests.Data;

namespace RunStats.Tests.Processing;

[Collection("Postgres")]
public sealed class RacePredictionUpdateTests(PostgresFixture postgres) : IAsyncLifetime
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    PooledDbContextFactory<AppDbContext> db = null!;
    RacePredictionUpdate update = null!;

    public async ValueTask InitializeAsync()
    {
        db = await MigratedDatabase.CreateAsync(postgres);
        update = new RacePredictionUpdate(db);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    static RacePrediction Prediction(int typeId, double seconds, int day = 5) =>
        new() { TypeId = typeId, Seconds = seconds, PredictedOn = new DateOnly(2026, 11, day) };

    async Task<List<RacePrediction>> StoredAsync()
    {
        await using var context = await db.CreateDbContextAsync(Ct);
        return await context.RacePredictions.AsNoTracking().OrderBy(p => p.TypeId).ToListAsync(Ct);
    }

    [Fact]
    public async Task First_predictions_are_added()
    {
        await update.ApplyAsync([Prediction(3, 1310), Prediction(4, 2806)], Ct);

        Assert.Equal([(3, 1310.0), (4, 2806.0)], (await StoredAsync()).Select(p => (p.TypeId, p.Seconds)));
    }

    [Fact]
    public async Task A_new_prediction_replaces_the_old_one()
    {
        await update.ApplyAsync([Prediction(3, 1310)], Ct);

        await update.ApplyAsync([Prediction(3, 1290, day: 6)], Ct);

        var stored = Assert.Single(await StoredAsync());
        Assert.Equal((1290.0, new DateOnly(2026, 11, 6)), (stored.Seconds, stored.PredictedOn));
    }

    [Fact]
    public async Task Distances_Garmin_no_longer_reports_keep_their_last_prediction()
    {
        await update.ApplyAsync([Prediction(3, 1310), Prediction(4, 2806)], Ct);

        await update.ApplyAsync([Prediction(4, 2790, day: 6)], Ct);
        await update.ApplyAsync([], Ct);

        var stored = await StoredAsync();
        Assert.Equal([(3, 1310.0), (4, 2790.0)], stored.Select(p => (p.TypeId, p.Seconds)));
    }
}
