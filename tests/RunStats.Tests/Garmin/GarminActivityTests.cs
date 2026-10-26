using System.Text.Json;
using RunStats.Garmin;

namespace RunStats.Tests.Garmin;

public class GarminActivityTests
{
    static GarminActivity Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return GarminActivity.FromJson(document.RootElement);
    }

    [Fact]
    public void Reads_the_fields_the_sync_needs_and_keeps_the_entry()
    {
        const string json = """
            {"activityId":21000000001,"activityName":"Lunch Run","startTimeLocal":"2026-10-25 10:30:00","startTimeGMT":"2026-10-25 09:30:00",
             "activityType":{"typeId":1,"typeKey":"running","parentTypeId":17},"eventType":{"typeId":1,"typeKey":"race"},
             "distance":10012.5,"duration":2700.25,"averageHR":162.0}
            """;

        var activity = Parse(json);

        Assert.Equal(21_000_000_001, activity.Id);
        Assert.Equal("Lunch Run", activity.Name);
        Assert.Equal(("running", 1, 17), (activity.TypeKey, activity.TypeId, activity.ParentTypeId));
        Assert.True(activity.IsRace);
        Assert.Equal(new DateTime(2026, 10, 25, 9, 30, 0, DateTimeKind.Utc), activity.StartUtc);
        Assert.Equal(DateTimeKind.Utc, activity.StartUtc.Kind);
        Assert.Equal(new DateTime(2026, 10, 25, 10, 30, 0), activity.StartLocal);
        Assert.Equal(DateTimeKind.Unspecified, activity.StartLocal.Kind);
        Assert.Equal((10012.5, 2700.25), (activity.DistanceMeters, activity.DurationSeconds));
        Assert.Equal(JsonDocument.Parse(json).RootElement.GetRawText(), activity.Json);
    }

    [Fact]
    public void Missing_or_null_optional_fields_get_defaults()
    {
        var activity = Parse("""
            {"activityId":5,"activityName":null,"startTimeLocal":"2026-10-25 10:30:00","startTimeGMT":"2026-10-25 09:30:00",
             "activityType":{"typeId":1,"typeKey":"running"},"eventType":null,"distance":null}
            """);

        Assert.Equal("", activity.Name);
        Assert.Equal(0, activity.ParentTypeId);
        Assert.False(activity.IsRace);
        Assert.Equal((0, 0), (activity.DistanceMeters, activity.DurationSeconds));
    }
}
