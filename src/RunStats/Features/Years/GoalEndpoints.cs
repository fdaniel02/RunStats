using System.Globalization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using RunStats.Data;

namespace RunStats.Features.Years;

public static class GoalEndpoints
{
    public const double MinKm = 1;
    public const double MaxKm = 20_000;
    const int FirstYear = 2000;
    const int LastYear = 2100;

    // The year page's forms post here. Afterwards the browser goes back to the year page, so reloading it doesn't post
    // again.
    public static IEndpointRouteBuilder MapGoalEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/years/{year:int}/goal", PostAsync);
        return app;
    }

    static async Task<IResult> PostAsync(int year, HttpContext context, IAntiforgery antiforgery, IDbContextFactory<AppDbContext> dbFactory,
        CancellationToken cancellationToken)
    {
        // Checked here rather than through [FromForm]: with a bad token, form binding throws, and the error page then
        // fails on the same form instead of answering 400.
        if (!context.Request.HasFormContentType || !await antiforgery.IsRequestValidAsync(context))
            return Results.BadRequest();
        if (year is < FirstYear or > LastYear)
            return Results.NotFound();
        var form = await context.Request.ReadFormAsync(cancellationToken);
        double? km = null;
        if (form["remove"] != "true")
        {
            if (!double.TryParse(form["km"], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value is not (>= MinKm and <= MaxKm))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["km"] = [$"Use {MinKm:0} to {MaxKm:0} km."] });
            km = value;
        }
        await SaveAsync(dbFactory, year, km * 1000, cancellationToken);
        return Results.LocalRedirect($"{context.Request.PathBase}/years/{year}");
    }

    // Null removes the goal.
    static async Task SaveAsync(IDbContextFactory<AppDbContext> dbFactory, int year, double? meters, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var goal = await db.YearGoals.FindAsync([year], cancellationToken);
        if (meters is not { } distance)
        {
            if (goal is not null)
                db.YearGoals.Remove(goal);
        }
        else if (goal is null)
            db.YearGoals.Add(new YearGoal { Year = year, DistanceMeters = distance });
        else
            goal.DistanceMeters = distance;
        await db.SaveChangesAsync(cancellationToken);
    }
}
