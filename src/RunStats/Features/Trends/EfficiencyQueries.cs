using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RunStats.Data;

namespace RunStats.Features.Trends;

// PaceSecondsPerKm is null for a month with too few minutes in the band. TemperatureCelsius is the weather of the
// month's runs in the band, weighted by their minutes in it.
public sealed record EfficiencyMonth(DateOnly Month, double Seconds, int Runs, double? PaceSecondsPerKm, double? TemperatureCelsius);

public sealed record EfficiencyData(int HeartRateFrom, int HeartRateTo, double MonthMinMinutes, IReadOnlyList<EfficiencyMonth> Months);

public interface IEfficiencyQueries
{
    Task<EfficiencyData> LoadAsync(CancellationToken cancellationToken);
}

// Outdoor road runs only, like the form at the same pace. Months go by the run's own (Garmin local) date, from the first
// month with time in the band to this month.
public class EfficiencyQueries(IDbContextFactory<AppDbContext> dbFactory, IOptions<EfficiencySettings> options, Clock clock) : IEfficiencyQueries
{
    public async Task<EfficiencyData> LoadAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var runs = await db.Runs
            .Where(r => r.TypeKey == "running")
            .Select(r => new
            {
                r.StartLocal,
                r.WeatherTemperatureCelsius,
                Seconds = r.HeartRateBands.Where(b => b.HeartRate >= settings.HeartRateFrom && b.HeartRate < settings.HeartRateTo).Sum(b => (int?)b.Seconds) ?? 0,
                Distance = r.HeartRateBands.Where(b => b.HeartRate >= settings.HeartRateFrom && b.HeartRate < settings.HeartRateTo).Sum(b => (double?)b.DistanceMeters) ?? 0
            })
            .Where(r => r.Seconds > 0)
            .ToListAsync(cancellationToken);

        var months = new List<EfficiencyMonth>();
        if (runs.Count > 0)
        {
            var byMonth = runs.ToLookup(r => new DateOnly(r.StartLocal.Year, r.StartLocal.Month, 1));
            var today = clock.Today;
            for (var month = byMonth.Min(g => g.Key); month <= new DateOnly(today.Year, today.Month, 1); month = month.AddMonths(1))
            {
                var inMonth = byMonth[month].ToList();
                var seconds = inMonth.Sum(r => r.Seconds);
                var distance = inMonth.Sum(r => r.Distance);
                var withWeather = inMonth.Where(r => r.WeatherTemperatureCelsius is not null).ToList();
                months.Add(new EfficiencyMonth(
                    month,
                    seconds,
                    inMonth.Count,
                    seconds >= settings.MonthMinMinutes * 60 && distance > 0 ? seconds / (distance / 1000) : null,
                    withWeather.Count > 0 ? withWeather.Sum(r => r.WeatherTemperatureCelsius!.Value * r.Seconds) / withWeather.Sum(r => r.Seconds) : null));
            }
        }
        return new EfficiencyData(settings.HeartRateFrom, settings.HeartRateTo, settings.MonthMinMinutes, months);
    }
}
