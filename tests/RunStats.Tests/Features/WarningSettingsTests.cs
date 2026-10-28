using System.ComponentModel.DataAnnotations;
using RunStats.Features.Warnings;

namespace RunStats.Tests.Features;

public class WarningSettingsTests
{
    static List<ValidationResult> Validate(WarningSettings settings)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(settings, new ValidationContext(settings), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void Defaults_are_valid() => Assert.Empty(Validate(new WarningSettings()));

    [Fact]
    public void Values_out_of_range_are_rejected()
    {
        var results = Validate(new WarningSettings { FastRatio = 0, FastPaceSecondsPerKm = 1000, BalanceShiftPoints = -1 });

        Assert.Equal(["BalanceShiftPoints", "FastPaceSecondsPerKm", "FastRatio"], results.SelectMany(r => r.MemberNames).Order());
    }
}
