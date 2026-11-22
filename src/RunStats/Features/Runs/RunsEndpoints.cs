using Microsoft.EntityFrameworkCore;
using RunStats.Data;

namespace RunStats.Features.Runs;

public sealed record RunApiRow(long Id, DateOnly Date, DateTime StartUtc, string Name, string TypeKey, double DistanceMeters,
    double DurationSeconds, double? Calories, double? TrainingLoad, short? AverageHeartRate);

public static class RunsEndpoints
{
    public const int MaxDays = 366;

    public static IEndpointRouteBuilder MapRunsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/runs", GetAsync);
        return app;
    }

    // Dates are in the app's time zone, like the dashboard's totals; both ends are included.
    static async Task<IResult> GetAsync(DateOnly from, DateOnly to, Clock clock, IDbContextFactory<AppDbContext> dbFactory,
        CancellationToken cancellationToken)
    {
        if (to < from || to.DayNumber - from.DayNumber >= MaxDays)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["to"] = [$"Use a range of 1 to {MaxDays} days, with to on or after from."] });
        var startUtc = clock.StartOfDayUtc(from);
        var endUtc = clock.StartOfDayUtc(to.AddDays(1));
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var runs = await db.Runs.AsNoTracking()
            .Where(r => r.StartUtc >= startUtc && r.StartUtc < endUtc)
            .OrderBy(r => r.StartUtc)
            .Select(r => new { r.Id, r.StartUtc, r.Name, r.TypeKey, r.DistanceMeters, r.DurationSeconds, r.Calories, r.TrainingLoad, r.AverageHeartRate })
            .ToListAsync(cancellationToken);
        return Results.Ok(runs.Select(r => new RunApiRow(r.Id, clock.ToLocalDate(r.StartUtc), DateTime.SpecifyKind(r.StartUtc, DateTimeKind.Utc), r.Name,
            r.TypeKey, r.DistanceMeters, r.DurationSeconds, r.Calories, r.TrainingLoad, r.AverageHeartRate)));
    }
}
