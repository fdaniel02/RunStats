using System.ComponentModel.DataAnnotations;
using RunStats.Features.Trends;

namespace RunStats.Tests.Features;

public class EfficiencySettingsTests
{
    static List<string> Invalid(EfficiencySettings settings)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(settings, new ValidationContext(settings), results, validateAllProperties: true);
        return [.. results.SelectMany(r => r.MemberNames).Order()];
    }

    [Fact]
    public void Defaults_are_valid() => Assert.Empty(Invalid(new EfficiencySettings()));

    [Fact]
    public void Values_out_of_range_are_rejected() =>
        Assert.Equal(["HeartRateFrom", "MonthMinMinutes"], Invalid(new EfficiencySettings { HeartRateFrom = 20, MonthMinMinutes = -1 }));

    [Fact]
    public void The_band_must_end_above_its_start() =>
        Assert.Equal(["HeartRateTo"], Invalid(new EfficiencySettings { HeartRateFrom = 160, HeartRateTo = 160 }));

    [Fact]
    public void The_band_must_be_whole_stored_bands() =>
        Assert.Equal(["HeartRateFrom", "HeartRateTo"], Invalid(new EfficiencySettings { HeartRateFrom = 152, HeartRateTo = 160 }));
}
