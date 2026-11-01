using System.ComponentModel.DataAnnotations;
using RunStats.Processing;

namespace RunStats.Features.Trends;

// The default band is where most of the easy and steady running was up to 2026-11: about 1000 of 3700 minutes.
public sealed class EfficiencySettings : IValidatableObject
{
    public const string Section = "Efficiency";

    // From inclusive, to exclusive; whole heart-rate bands, as the runs store them.
    [Range(40, 230)] public int HeartRateFrom { get; set; } = 150;
    [Range(40, 230)] public int HeartRateTo { get; set; } = 160;
    // Fewer minutes in the band make a month's pace jump around.
    [Range(0, 600)] public double MonthMinMinutes { get; set; } = 30;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (HeartRateTo <= HeartRateFrom)
            yield return new ValidationResult("The band must end above its start.", [nameof(HeartRateTo)]);
        if (HeartRateFrom % HeartRateBandBuilder.BandWidthBpm != 0 || HeartRateTo % HeartRateBandBuilder.BandWidthBpm != 0)
            yield return new ValidationResult($"The band must start and end on a multiple of {HeartRateBandBuilder.BandWidthBpm} bpm.",
                [nameof(HeartRateFrom), nameof(HeartRateTo)]);
    }
}
