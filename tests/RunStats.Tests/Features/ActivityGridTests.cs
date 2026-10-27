using RunStats.Features.Dashboard;

namespace RunStats.Tests.Features;

public class ActivityGridTests
{
    static DateOnly Date(string s) => DateOnly.Parse(s);

    static Dictionary<int, string> Labels(ActivityGrid grid) =>
        grid.Weeks.Select((w, i) => (w.MonthLabel, i))
            .Where(x => x.MonthLabel is not null)
            .ToDictionary(x => x.i, x => x.MonthLabel!);

    [Theory]
    [InlineData("2026-10-12")] // Monday
    [InlineData("2026-10-15")] // Thursday
    [InlineData("2026-10-18")] // Sunday
    public void Last_column_is_the_current_week_and_columns_run_monday_to_sunday(string today)
    {
        var grid = new ActivityGrid(Date(today), 16);

        Assert.Equal(Date("2026-06-29"), grid.Start);
        Assert.Equal(16, grid.Weeks.Count);
        Assert.All(grid.Weeks, w => Assert.Equal(DayOfWeek.Monday, w.Days[0].DayOfWeek));
        Assert.Equal(
            Enumerable.Range(0, 16 * 7).Select(grid.Start.AddDays),
            grid.Weeks.SelectMany(w => w.Days));
        Assert.Contains(Date(today), grid.Weeks[^1].Days);
    }

    [Fact]
    public void Month_label_is_on_the_column_that_contains_the_first()
    {
        // Columns from Mon 29 Jun 2026; 1 Jul is in column 0, 1 Aug (Sat) in 4, 1 Sep in 9, 1 Oct in 13.
        var grid = new ActivityGrid(Date("2026-10-15"), 16);

        Assert.Equal(new Dictionary<int, string> { [0] = "Jul", [4] = "Aug", [9] = "Sep", [13] = "Oct" }, Labels(grid));
    }

    [Fact]
    public void First_of_the_month_on_a_monday_or_sunday_labels_that_column()
    {
        // 1 Jun 2026 is a Monday, 1 Mar 2026 a Sunday.
        var grid = new ActivityGrid(Date("2026-06-03"), 16);

        var labels = Labels(grid);
        Assert.Equal("Mar", labels[grid.Weeks.ToList().FindIndex(w => w.Days[^1] == Date("2026-03-01"))]);
        Assert.Equal("Jun", labels[grid.Weeks.ToList().FindIndex(w => w.Days[0] == Date("2026-06-01"))]);
    }

    [Fact]
    public void First_column_is_labelled_when_no_month_starts_in_it()
    {
        // Start Mon 6 Jul 2026: no 1st in column 0 or 1.
        var grid = new ActivityGrid(Date("2026-10-22"), 16);

        Assert.Equal(Date("2026-07-06"), grid.Start);
        Assert.Equal("Jul", Labels(grid)[0]);
    }

    [Fact]
    public void First_column_is_unlabelled_when_a_month_starts_in_the_next_column()
    {
        // Start Mon 20 Jul 2026: column 1 (27 Jul - 2 Aug) contains 1 Aug, so "Jul" would overlap "Aug".
        var grid = new ActivityGrid(Date("2026-11-05"), 16);

        Assert.Equal(Date("2026-07-20"), grid.Start);
        var labels = Labels(grid);
        Assert.False(labels.ContainsKey(0));
        Assert.Equal("Aug", labels[1]);
    }

    [Fact]
    public void Single_week_grid_is_labelled()
    {
        var grid = new ActivityGrid(Date("2026-10-15"), 1);

        Assert.Equal("Oct", Labels(grid)[0]);
    }

    [Fact]
    public void Labels_continue_across_the_year_boundary()
    {
        var grid = new ActivityGrid(Date("2027-02-10"), 16);

        Assert.Contains("Jan", Labels(grid).Values);
        Assert.Contains("Feb", Labels(grid).Values);
    }
}
