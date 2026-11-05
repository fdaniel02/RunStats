using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RunStats.Data;
using RunStats.Notion;

namespace RunStats.Sync;

// The local table is a copy of the Notion database: races removed there are removed here. Notion is optional for a sync:
// when it can't be read, the Garmin data is still good and the page keeps the last copy.
public class UpcomingRacesSync(INotionRaces notion, IDbContextFactory<AppDbContext> dbFactory, Clock clock, ILogger<UpcomingRacesSync> logger)
{
    public const int MaxNameLength = 200;
    public const int MaxCityLength = 100;
    public const int MaxTypeLength = 50;
    public const int MaxLinkLength = 2000;

    public async Task UpdateAsync(int syncId, CancellationToken cancellationToken)
    {
        if (!notion.IsConfigured)
        {
            logger.UpcomingRacesNotConfigured(syncId);
            return;
        }
        IReadOnlyList<NotionRace> races;
        try
        {
            races = await notion.GetFromAsync(clock.Today, cancellationToken);
        }
        // A timeout is also an OperationCanceledException, without the sync being cancelled.
        catch (Exception e) when (e is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException
            || (e is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.UpcomingRacesFailed(syncId, e.GetType().Name, e.Message);
            return;
        }
        await StoreAsync(syncId, races, cancellationToken);
        logger.UpcomingRacesUpdated(syncId, races.Count);
    }

    async Task StoreAsync(int syncId, IReadOnlyList<NotionRace> races, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var stored = await db.UpcomingRaces.ToDictionaryAsync(r => r.NotionPageId, cancellationToken);
        foreach (var race in races)
        {
            if (!stored.Remove(race.Id, out var row))
            {
                row = new UpcomingRace { NotionPageId = race.Id };
                db.UpcomingRaces.Add(row);
            }
            row.Name = Shortened(race.Name, MaxNameLength)!;
            row.Date = race.Date;
            row.StartTime = race.StartTime;
            // Rounded because 16.1 * 1000 is 16100.000000000002 as a double, and the value ends up in the calculator's link.
            row.DistanceMeters = race.DistanceKm is { } km ? Math.Round(km * 1000, 1) : null;
            row.Type = Shortened(race.Type, MaxTypeLength);
            row.IsRegistered = race.IsRegistered;
            row.City = Shortened(race.City, MaxCityLength);
            row.Link = race.Link;
            row.TargetSeconds = race.TargetTime is { } text ? TargetTime.ParseSeconds(text) : null;
            if (race.TargetTime is not null && row.TargetSeconds is null)
                logger.UpcomingRaceTargetUnreadable(syncId, row.Name, race.TargetTime);
        }
        db.UpcomingRaces.RemoveRange(stored.Values);
        await db.SaveChangesAsync(cancellationToken);
    }

    static string? Shortened(string? text, int maxLength) => text is { } t && t.Length > maxLength ? t[..maxLength] : text;
}
