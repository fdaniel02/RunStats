using System.Globalization;
using System.Text.Json;

namespace RunStats.Garmin;

// One entry of Garmin's activity list, with the fields the sync needs and the entry as it came.
public sealed record GarminActivity(
    long Id,
    string Name,
    string TypeKey,
    int TypeId,
    int ParentTypeId,
    bool IsRace,
    DateTime StartUtc,
    DateTime StartLocal,
    double DistanceMeters,
    double DurationSeconds,
    string Json)
{
    const string GarminTimeFormat = "yyyy-MM-dd HH:mm:ss";

    public static GarminActivity FromJson(JsonElement e)
    {
        var type = e.GetProperty("activityType");
        return new GarminActivity(
            e.GetProperty("activityId").GetInt64(),
            OptionalString(e, "activityName") ?? "",
            type.GetProperty("typeKey").GetString() ?? "",
            type.GetProperty("typeId").GetInt32(),
            type.TryGetProperty("parentTypeId", out var parent) && parent.ValueKind == JsonValueKind.Number ? parent.GetInt32() : 0,
            e.TryGetProperty("eventType", out var eventType) && OptionalString(eventType, "typeKey") == "race",
            ParseTime(e, "startTimeGMT", DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
            ParseTime(e, "startTimeLocal", DateTimeStyles.None),
            OptionalDouble(e, "distance"),
            OptionalDouble(e, "duration"),
            e.GetRawText());
    }

    static string? OptionalString(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    static double OptionalDouble(JsonElement e, string name) =>
        e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : 0;

    static DateTime ParseTime(JsonElement e, string name, DateTimeStyles styles) =>
        DateTime.ParseExact(e.GetProperty(name).GetString()!, GarminTimeFormat, CultureInfo.InvariantCulture, styles);
}
