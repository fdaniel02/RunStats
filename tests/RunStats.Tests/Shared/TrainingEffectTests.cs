using RunStats.Shared;

namespace RunStats.Tests.Shared;

public class TrainingEffectTests
{
    [Theory]
    [InlineData("RECOVERY", 1, "Recovery")]
    [InlineData("AEROBIC_BASE", 2, "Base")]
    [InlineData("TEMPO", 3, "Tempo")]
    [InlineData("LACTATE_THRESHOLD", 4, "Threshold")]
    [InlineData("VO2MAX", 5, "VO2 max")]
    [InlineData("ANAEROBIC_CAPACITY", 6, "Anaerobic")]
    [InlineData("SPRINT", 6, "Sprint")]
    [InlineData("tempo", 3, "Tempo")]
    public void Garmins_labels_have_a_level_and_a_name(string label, int level, string name)
    {
        Assert.Equal(level, TrainingEffect.Level(label));
        Assert.Equal(name, TrainingEffect.Name(label));
        Assert.Equal($"effect-{level}", TrainingEffect.CssClass(label));
    }

    [Fact]
    public void A_new_label_gets_a_readable_name_but_no_level()
    {
        Assert.Equal("No benefit", TrainingEffect.Name("NO_BENEFIT"));
        Assert.Null(TrainingEffect.Level("NO_BENEFIT"));
        Assert.Null(TrainingEffect.CssClass("NO_BENEFIT"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void No_label_has_no_name_or_level(string? label)
    {
        Assert.Null(TrainingEffect.Name(label));
        Assert.Null(TrainingEffect.Level(label));
    }

    [Theory]
    [InlineData("TEMPO", "RECOVERY", "TEMPO")]
    [InlineData("RECOVERY", "VO2MAX", "VO2MAX")]
    [InlineData("TEMPO", "TEMPO", "TEMPO")]
    [InlineData(null, "TEMPO", "TEMPO")]
    [InlineData("TEMPO", null, "TEMPO")]
    [InlineData("NO_BENEFIT", "RECOVERY", "RECOVERY")]
    [InlineData("NO_BENEFIT", null, "NO_BENEFIT")]
    [InlineData(null, null, null)]
    public void The_harder_label_wins(string? a, string? b, string? expected) => Assert.Equal(expected, TrainingEffect.Harder(a, b));
}
