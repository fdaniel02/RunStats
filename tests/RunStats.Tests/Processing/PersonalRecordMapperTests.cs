using System.Text.Json;
using RunStats.Processing;

namespace RunStats.Tests.Processing;

public class PersonalRecordMapperTests
{
    [Fact]
    public void Running_records_are_kept_with_their_run_and_times()
    {
        // The 5 km record started 10 minutes into an activity that began at 09:50 UTC, 10:50 local time.
        const string json = """
            [{"id":1,"typeId":3,"status":"ACCEPTED","activityId":21000000001,"activityName":"Interval","activityType":"running",
              "activityStartDateTimeInGMT":1761385800000,"activityStartDateTimeLocal":1761389400000,
              "value":1250.5,"prStartTimeGmt":1761386400000,"prStartTimeLocal":null}]
            """;

        var record = Assert.Single(PersonalRecordMapper.Running(json));

        Assert.Equal((3, 1250.5, 21_000_000_001L), (record.TypeId, record.Value, record.RunId!.Value));
        Assert.Equal(new DateTime(2025, 10, 25, 10, 0, 0, DateTimeKind.Utc), record.AchievedAtUtc);
        Assert.Equal(DateTimeKind.Utc, record.AchievedAtUtc.Kind);
        Assert.Equal(new DateTime(2025, 10, 25, 11, 0, 0), record.AchievedAtLocal);
        Assert.Equal(DateTimeKind.Unspecified, record.AchievedAtLocal.Kind);
        Assert.Equal(JsonDocument.Parse(json).RootElement[0].GetRawText(), record.RawJson);
    }

    [Fact]
    public void Other_sports_and_records_without_a_sport_are_left_out()
    {
        const string json = """
            [{"typeId":8,"activityType":"road_biking","value":40000.0,"prStartTimeGmt":1761385800000},
             {"typeId":12,"activityType":null,"value":25000.0,"prStartTimeGmt":1761385800000},
             {"typeId":13,"value":90000.0,"prStartTimeGmt":1761385800000},
             "not a record",
             {"typeId":7,"activityType":"running","value":21097.5,"prStartTimeGmt":1761385800000}]
            """;

        Assert.Equal([7], PersonalRecordMapper.Running(json).Select(r => r.TypeId));
    }

    [Fact]
    public void Without_its_own_start_the_record_uses_the_activitys()
    {
        var record = Assert.Single(PersonalRecordMapper.Running("""
            [{"typeId":7,"activityType":"running","value":21097.5,"activityStartDateTimeInGMT":1761385800000}]
            """));

        Assert.Equal(new DateTime(2025, 10, 25, 9, 50, 0, DateTimeKind.Utc), record.AchievedAtUtc);
        Assert.Equal(new DateTime(2025, 10, 25, 9, 50, 0), record.AchievedAtLocal);
        Assert.Null(record.RunId);
    }

    [Theory]
    [InlineData("""{"activityType":"running","value":1.0,"prStartTimeGmt":1761385800000}""")]
    [InlineData("""{"typeId":3,"activityType":"running","prStartTimeGmt":1761385800000}""")]
    [InlineData("""{"typeId":3,"activityType":"running","value":1.0}""")]
    [InlineData("""{"typeId":3,"activityType":"running","value":"fast","prStartTimeGmt":1761385800000}""")]
    public void Records_without_type_value_or_time_are_left_out(string record) =>
        Assert.Empty(PersonalRecordMapper.Running($"[{record}]"));

    [Fact]
    public void Something_other_than_a_list_is_an_error()
    {
        var error = Assert.Throws<FormatException>(() => PersonalRecordMapper.Running("""{"error":"x"}"""));

        Assert.Equal("Garmin's personal records aren't a list.", error.Message);
    }
}
