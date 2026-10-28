using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RunStats.Data;

namespace RunStats.Features.Warnings;

public sealed record DayWarnings(DateOnly Date, IReadOnlyList<Warning> Warnings);

public interface IWarningQueries
{
    Task<List<Warning>> ActiveAsync(CancellationToken cancellationToken);
    // Each day evaluated at its end in the app's time zone; days without warnings are left out.
    Task<List<DayWarnings>> DailyAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);
    // The warnings at each moment, in the same order.
    Task<List<List<Warning>>> AtAsync(IReadOnlyList<DateTime> momentsUtc, CancellationToken cancellationToken);
}

public class WarningQueries(IDbContextFactory<AppDbContext> dbFactory, IOptions<WarningSettings> settings, Clock clock, TimeProvider time)
    : IWarningQueries
{
    public async Task<List<Warning>> ActiveAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var runs = await RunsAsync(now - WarningRules.History, now, cancellationToken);
        return WarningRules.Evaluate(runs, now, settings.Value);
    }

    public async Task<List<DayWarnings>> DailyAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var runs = await RunsAsync(clock.StartOfDayUtc(from) - WarningRules.History, clock.StartOfDayUtc(to.AddDays(1)), cancellationToken);
        var days = new List<DayWarnings>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var warnings = WarningRules.Evaluate(runs, clock.StartOfDayUtc(day.AddDays(1)), settings.Value);
            if (warnings.Count > 0)
                days.Add(new DayWarnings(day, warnings));
        }
        return days;
    }

    public async Task<List<List<Warning>>> AtAsync(IReadOnlyList<DateTime> momentsUtc, CancellationToken cancellationToken)
    {
        if (momentsUtc.Count == 0)
            return [];
        var runs = await RunsAsync(momentsUtc.Min() - WarningRules.History, momentsUtc.Max(), cancellationToken);
        return [.. momentsUtc.Select(m => WarningRules.Evaluate(runs, m, settings.Value))];
    }

    async Task<List<WarningRun>> RunsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Runs.AsNoTracking()
            .Where(r => r.StartUtc > fromUtc && r.StartUtc <= toUtc)
            .Select(r => new WarningRun(r.StartUtc, r.StartLocal, r.TypeKey, r.DurationSeconds, r.PaceBands.ToList()))
            .ToListAsync(cancellationToken);
    }
}
