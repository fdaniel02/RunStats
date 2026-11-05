using RunStats.Features.Years;

namespace RunStats.Tests.Features;

public class RunTimesTests
{
    static RunTime Run(int hour, int minute, double minutes, int day = 7) => new(new DateTime(2026, 10, day, hour, minute, 0), minutes * 60);

    [Fact]
    public void Distances_round_to_the_nearest_km_with_halves_up()
    {
        Assert.Equal(
            [new DistanceCount(1, 0), new DistanceCount(2, 2), new DistanceCount(3, 1)],
            DistanceCounts.Of([1500, 2499, 2500]));
    }

    [Fact]
    public void Distances_without_runs_stay_on_the_axis()
    {
        Assert.Equal([0, 1, 0, 1], DistanceCounts.Of([2000, 4000]).Select(c => c.Runs));
    }

    [Fact]
    public void A_very_short_run_counts_as_a_km_and_one_without_distance_not_at_all()
    {
        Assert.Equal([new DistanceCount(1, 1)], DistanceCounts.Of([200, 0]));
        Assert.Empty(DistanceCounts.Of([0]));
    }

    [Fact]
    public void The_window_is_the_whole_hours_around_the_runs()
    {
        Assert.Equal(new DayWindow(8, 20), DayWindow.Of([Run(8, 30, 10), Run(19, 30, 20)]));
        Assert.Equal(new DayWindow(9, 10), DayWindow.Of([Run(9, 0, 0)]));
        Assert.Null(DayWindow.Of([]));
    }

    [Fact]
    public void A_run_past_midnight_is_cut_at_the_end_of_the_day()
    {
        var run = Run(23, 30, 60);

        Assert.Equal(new DayWindow(23, 24), DayWindow.Of([run]));
        Assert.Equal([0, 1], new DayWindow(23, 24).RunsInProgress([run]));
    }

    [Fact]
    public void A_run_counts_in_every_slot_it_overlaps()
    {
        var window = new DayWindow(18, 20);

        Assert.Equal([1, 1, 0, 0], window.RunsInProgress([Run(18, 10, 40)]));
        Assert.Equal([0, 2, 1, 0], window.RunsInProgress([Run(18, 30, 40), Run(18, 30, 10)]));
    }

    [Fact]
    public void A_run_ending_on_a_slots_start_doesnt_count_in_that_slot()
    {
        Assert.Equal([1, 0], new DayWindow(18, 19).RunsInProgress([Run(18, 0, 30)]));
    }

    [Fact]
    public void Weekdays_count_from_monday()
    {
        // 5 October 2026 was a Monday.
        Assert.Equal([0, 6], new[] { Run(9, 0, 10, day: 5), Run(9, 0, 10, day: 11) }.Select(r => r.DaysFromMonday));
    }

    [Fact]
    public void Slots_are_named_by_their_clock_times()
    {
        Assert.Equal("08:30–09:00", new DayWindow(8, 10).SlotName(1));
        Assert.Equal("23:30–24:00", new DayWindow(23, 24).SlotName(1));
    }

    [Fact]
    public void Ticks_fall_on_round_hours_every_three_or_every_hour_in_a_short_window()
    {
        Assert.Equal([9, 12, 15, 18, 21], new DayWindow(8, 21).TickHours());
        Assert.Equal([17, 18, 19], new DayWindow(17, 19).TickHours());
    }

    [Fact]
    public void Fraction_places_a_minute_within_the_window()
    {
        Assert.Equal(.25, new DayWindow(10, 12).Fraction(10.5 * 60));
    }
}
