using Microsoft.EntityFrameworkCore;
using RunStats.Data;

namespace RunStats.Processing;

public sealed record PersonalRecordChanges(int Added, int Changed, int Removed);

// Makes the stored personal records the same as Garmin's current ones.
public class PersonalRecordUpdate(IDbContextFactory<AppDbContext> dbFactory, TimeProvider time)
{
    public async Task<PersonalRecordChanges> ApplyAsync(IReadOnlyList<PersonalRecord> current, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var stored = await db.PersonalRecords.ToDictionaryAsync(r => r.TypeId, cancellationToken);
        var now = time.GetUtcNow().UtcDateTime;
        var (added, changed) = (0, 0);
        foreach (var record in current.DistinctBy(r => r.TypeId))
        {
            if (!stored.Remove(record.TypeId, out var existing))
            {
                record.UpdatedAtUtc = now;
                db.PersonalRecords.Add(record);
                added++;
                continue;
            }
            // UpdatedAtUtc is when the record last changed, so it isn't touched when only the JSON's field order did.
            if (existing.Value != record.Value || existing.RunId != record.RunId || existing.AchievedAtUtc != record.AchievedAtUtc)
            {
                existing.UpdatedAtUtc = now;
                changed++;
            }
            existing.Value = record.Value;
            existing.RunId = record.RunId;
            existing.AchievedAtUtc = record.AchievedAtUtc;
            existing.AchievedAtLocal = record.AchievedAtLocal;
            existing.RawJson = record.RawJson;
        }
        db.PersonalRecords.RemoveRange(stored.Values);
        await db.SaveChangesAsync(cancellationToken);
        return new PersonalRecordChanges(added, changed, stored.Count);
    }
}
