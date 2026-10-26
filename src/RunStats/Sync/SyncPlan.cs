using System.Text.Json;
using RunStats.Garmin;

namespace RunStats.Sync;

public static class RunFilter
{
    const int RunningTypeId = 1;

    // Plain "running" is type 1 under "all" (17); treadmill, trail, track, obstacle runs etc. have parent 1.
    public static bool IsRun(int typeId, int parentTypeId) => typeId == RunningTypeId || parentTypeId == RunningTypeId;
}

// What a sync has to do, from Garmin's whole activity list and the list entries stored last time.
public sealed record SyncPlan(IReadOnlyList<GarminActivity> ToAdd, IReadOnlyList<GarminActivity> ToUpdate, IReadOnlyList<long> ToDelete, int Unchanged)
{
    public IEnumerable<GarminActivity> Downloads => ToAdd.Concat(ToUpdate);

    public static SyncPlan Create(IEnumerable<GarminActivity> garminActivities, IReadOnlyDictionary<long, string> storedListJson)
    {
        // A run added while the list is paged shifts the pages, so the same entry can show up twice.
        var runs = garminActivities.Where(a => RunFilter.IsRun(a.TypeId, a.ParentTypeId)).DistinctBy(a => a.Id).ToList();
        var toAdd = runs.Where(r => !storedListJson.ContainsKey(r.Id)).ToList();
        var toUpdate = runs.Where(r => storedListJson.TryGetValue(r.Id, out var stored) && !SameEntry(stored, r.Json)).ToList();
        var inGarmin = runs.Select(r => r.Id).ToHashSet();
        var toDelete = storedListJson.Keys.Where(id => !inGarmin.Contains(id)).Order().ToList();
        return new SyncPlan(toAdd, toUpdate, toDelete, runs.Count - toAdd.Count - toUpdate.Count);
    }

    // Garmin sends the fields of an entry in a different order on every call, so the text can't be compared.
    static bool SameEntry(string stored, string current)
    {
        if (stored.Length == 0)
            return false;
        using var storedDocument = JsonDocument.Parse(stored);
        using var currentDocument = JsonDocument.Parse(current);
        return JsonElement.DeepEquals(storedDocument.RootElement, currentDocument.RootElement);
    }
}
