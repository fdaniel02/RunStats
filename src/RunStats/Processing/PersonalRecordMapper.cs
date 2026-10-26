using System.Text.Json;
using RunStats.Data;

namespace RunStats.Processing;

public static class PersonalRecordMapper
{
    // Garmin's personal records for every sport, as the gateway returns them; only the running ones are kept.
    public static List<PersonalRecord> Running(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new FormatException("Garmin's personal records aren't a list.");
        return [.. document.RootElement.EnumerateArray().Where(IsRunning).Select(Map).OfType<PersonalRecord>()];
    }

    static bool IsRunning(JsonElement record) =>
        record.ValueKind == JsonValueKind.Object
        && record.TryGetProperty("activityType", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "running";

    static PersonalRecord? Map(JsonElement record)
    {
        if (Integer(record, "typeId") is not { } typeId || Number(record, "value") is not { } value
            || (Integer(record, "prStartTimeGmt") ?? Integer(record, "activityStartDateTimeInGMT")) is not { } startMs)
            return null;
        var achievedAtUtc = DateTimeOffset.FromUnixTimeMilliseconds(startMs).UtcDateTime;
        // Garmin leaves the record's own local time empty; the activity's local and UTC start give the offset.
        var offset = Integer(record, "activityStartDateTimeLocal") - Integer(record, "activityStartDateTimeInGMT") ?? 0;
        return new PersonalRecord
        {
            TypeId = (int)typeId,
            Value = value,
            RunId = Integer(record, "activityId"),
            AchievedAtUtc = achievedAtUtc,
            AchievedAtLocal = DateTime.SpecifyKind(achievedAtUtc.AddMilliseconds(offset), DateTimeKind.Unspecified),
            RawJson = record.GetRawText()
        };
    }

    static long? Integer(JsonElement e, string name) =>
        e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : null;

    static double? Number(JsonElement e, string name) =>
        e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;
}
