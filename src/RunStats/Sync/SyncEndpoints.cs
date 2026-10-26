using Microsoft.EntityFrameworkCore;
using RunStats.Data;

namespace RunStats.Sync;

public static class SyncEndpoints
{
    public static IEndpointRouteBuilder MapSyncEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/sync", async (SyncCoordinator coordinator, string? trigger, CancellationToken cancellationToken) =>
        {
            if (!TryParseTrigger(trigger, out var parsed))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["trigger"] = ["Use 'schedule' or 'manual'."] });
            var request = await coordinator.RequestAsync(parsed, cancellationToken);
            return Results.Accepted($"/api/sync/{request.SyncId}", request);
        });

        app.MapGet("/api/sync/{id:int}", async (int id, IDbContextFactory<AppDbContext> dbFactory, CancellationToken cancellationToken) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var sync = await db.SyncRuns.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
            return sync is null ? Results.NotFound() : Results.Ok(sync);
        });

        return app;
    }

    // By name only: Enum.TryParse would also accept numbers.
    static bool TryParseTrigger(string? value, out SyncTrigger trigger)
    {
        trigger = SyncTrigger.Schedule;
        if (string.IsNullOrEmpty(value))
            return true;
        foreach (var candidate in Enum.GetValues<SyncTrigger>())
        {
            if (string.Equals(value, candidate.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                trigger = candidate;
                return true;
            }
        }
        return false;
    }
}
