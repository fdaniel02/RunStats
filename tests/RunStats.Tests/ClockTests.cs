using RunStats.Data;

namespace RunStats.Tests;

public class ClockTests
{
    static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
    readonly Clock clock = new(Amsterdam, TimeProvider.System);

    [Theory]
    // Summer time (UTC+2): 22:30 UTC is already the next day in Amsterdam.
    [InlineData("2026-10-13T22:30:00", "2026-10-14")]
    [InlineData("2026-10-13T21:59:59", "2026-10-13")]
    // Winter time (UTC+1).
    [InlineData("2026-01-13T23:30:00", "2026-01-14")]
    [InlineData("2026-01-13T22:59:59", "2026-01-13")]
    public void ToLocalDate_uses_the_configured_time_zone(string utc, string expected)
    {
        var date = clock.ToLocalDate(DateTime.Parse(utc));

        Assert.Equal(DateOnly.Parse(expected), date);
    }

    [Theory]
    [InlineData("2026-10-24T18:01:00", "2026-10-24T20:01:00")]
    [InlineData("2026-10-26T19:01:00", "2026-10-26T20:01:00")]
    public void ToLocalTime_follows_summer_and_winter_time(string utc, string expected)
    {
        var fromDb = DateTime.SpecifyKind(DateTime.Parse(utc), DateTimeKind.Unspecified);

        Assert.Equal(DateTime.Parse(expected), clock.ToLocalTime(fromDb));
    }

    [Fact]
    public void ToLocalDate_treats_unspecified_kind_as_utc()
    {
        // EF Core returns DateTime values from SQL Server with Kind = Unspecified.
        var fromDb = new DateTime(2026, 10, 13, 22, 30, 0, DateTimeKind.Unspecified);

        Assert.Equal(new DateOnly(2026, 10, 14), clock.ToLocalDate(fromDb));
    }

    [Theory]
    [InlineData("2026-10-13T21:59:59Z", "2026-10-13")]
    [InlineData("2026-10-13T22:00:00Z", "2026-10-14")]
    public void Today_is_the_local_date_of_the_time_providers_now(string utcNow, string expected)
    {
        var fixedClock = new Clock(Amsterdam, new FixedTimeProvider(DateTimeOffset.Parse(utcNow)));

        Assert.Equal(DateOnly.Parse(expected), fixedClock.Today);
    }

    [Theory]
    [InlineData("2026-07-01", "2026-06-30T22:00:00")]
    [InlineData("2026-01-14", "2026-01-13T23:00:00")]
    public void StartOfDayUtc_is_local_midnight(string date, string expectedUtc)
    {
        var start = clock.StartOfDayUtc(DateOnly.Parse(date));

        Assert.Equal(DateTime.Parse(expectedUtc), start);
        Assert.Equal(DateTimeKind.Utc, start.Kind);
    }

    [Fact]
    public void StartOfDayUtc_skips_a_midnight_that_does_not_exist()
    {
        // Chile starts daylight saving at midnight: on 6 Sep 2026 the clock jumps from 00:00 (UTC-4) to 01:00 (UTC-3).
        var santiago = new Clock(TimeZoneInfo.FindSystemTimeZoneById("America/Santiago"), TimeProvider.System);

        Assert.Equal(new DateTime(2026, 9, 6, 4, 0, 0), santiago.StartOfDayUtc(new DateOnly(2026, 9, 6)));
    }
}
