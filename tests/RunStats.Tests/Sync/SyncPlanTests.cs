using RunStats.Sync;
using static RunStats.Tests.Sync.FakeGarminGateway;

namespace RunStats.Tests.Sync;

public class SyncPlanTests
{
    [Theory]
    [InlineData(1, 17, true)]      // running
    [InlineData(18, 1, true)]      // treadmill_running
    [InlineData(6, 1, true)]       // trail_running
    [InlineData(154, 1, true)]     // obstacle_run
    [InlineData(2, 17, false)]     // cycling
    [InlineData(10, 2, false)]     // road_biking
    [InlineData(9, 17, false)]     // walking
    public void Runs_are_running_itself_and_its_sub_types(int typeId, int parentTypeId, bool isRun) =>
        Assert.Equal(isRun, RunFilter.IsRun(typeId, parentTypeId));

    [Fact]
    public void Everything_is_new_on_the_first_sync()
    {
        var plan = SyncPlan.Create([Activity(1), Activity(2, "treadmill_running", 18, 1)], new Dictionary<long, string>());

        Assert.Equal([1L, 2L], plan.ToAdd.Select(a => a.Id));
        Assert.Empty(plan.ToUpdate);
        Assert.Empty(plan.ToDelete);
        Assert.Equal(0, plan.Unchanged);
    }

    [Fact]
    public void Other_sports_are_left_out()
    {
        var plan = SyncPlan.Create([Activity(1), Activity(2, "road_biking", 10, 2)], new Dictionary<long, string>());

        Assert.Equal([1L], plan.ToAdd.Select(a => a.Id));
    }

    [Fact]
    public void Stored_runs_are_updated_only_when_their_list_entry_changed()
    {
        var same = Activity(1);
        var renamed = Activity(2, name: "Renamed");
        var stored = new Dictionary<long, string> { [1] = same.Json, [2] = Activity(2).Json };

        var plan = SyncPlan.Create([same, renamed], stored);

        Assert.Empty(plan.ToAdd);
        Assert.Equal([2L], plan.ToUpdate.Select(a => a.Id));
        Assert.Equal(1, plan.Unchanged);
        Assert.Equal([2L], plan.Downloads.Select(a => a.Id));
    }

    [Fact]
    public void Same_entry_with_the_fields_in_another_order_is_unchanged()
    {
        var stored = new Dictionary<long, string> { [1] = """{"activityId":1,"distance":5000.0,"activityType":{"typeId":1,"parentTypeId":17}}""" };
        var reordered = Activity(1) with { Json = """{"activityType":{"parentTypeId":17,"typeId":1},"distance":5000.0,"activityId":1}""" };

        var plan = SyncPlan.Create([reordered], stored);

        Assert.Empty(plan.ToUpdate);
        Assert.Equal(1, plan.Unchanged);
    }

    [Fact]
    public void Run_stored_without_its_list_entry_is_downloaded_again()
    {
        var plan = SyncPlan.Create([Activity(1)], new Dictionary<long, string> { [1] = "" });

        Assert.Equal([1L], plan.ToUpdate.Select(a => a.Id));
    }

    [Fact]
    public void Stored_runs_that_are_gone_from_Garmin_are_deleted()
    {
        var stored = new Dictionary<long, string> { [3] = "{}", [1] = Activity(1).Json, [2] = "{}" };

        var plan = SyncPlan.Create([Activity(1)], stored);

        Assert.Equal([2L, 3L], plan.ToDelete);
    }

    [Fact]
    public void A_run_that_turned_into_another_sport_is_deleted()
    {
        var stored = new Dictionary<long, string> { [1] = Activity(1).Json };

        var plan = SyncPlan.Create([Activity(1, "road_biking", 10, 2)], stored);

        Assert.Equal([1L], plan.ToDelete);
    }

    [Fact]
    public void An_entry_listed_twice_counts_once()
    {
        var plan = SyncPlan.Create([Activity(1), Activity(1)], new Dictionary<long, string>());

        Assert.Single(plan.ToAdd);
    }
}
