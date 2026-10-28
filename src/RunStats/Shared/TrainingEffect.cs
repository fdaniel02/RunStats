namespace RunStats.Shared;

// Garmin's training effect labels in order of intensity; the level picks the shade of blue (--effect-1 to --effect-6 in
// app.css) and decides which run counts on a day with more than one.
public static class TrainingEffect
{
    public const int MaxLevel = 6;

    static readonly Dictionary<string, (int Level, string Name)> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["RECOVERY"] = (1, "Recovery"),
        ["AEROBIC_BASE"] = (2, "Base"),
        ["TEMPO"] = (3, "Tempo"),
        ["LACTATE_THRESHOLD"] = (4, "Threshold"),
        ["VO2MAX"] = (5, "VO2 max"),
        ["ANAEROBIC_CAPACITY"] = (6, "Anaerobic"),
        ["SPRINT"] = (6, "Sprint")
    };

    public static int? Level(string? label) => label is not null && Labels.TryGetValue(label, out var known) ? known.Level : null;

    // A label Garmin adds later still gets a readable name, just no level.
    public static string? Name(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return null;
        if (Labels.TryGetValue(label, out var known))
            return known.Name;
        var words = label.Replace('_', ' ').Trim().ToLowerInvariant();
        return char.ToUpperInvariant(words[0]) + words[1..];
    }

    public static string? CssClass(string? label) => Level(label) is { } level ? $"effect-{level}" : null;

    public static string? Harder(string? a, string? b) => (Level(b) ?? 0) > (Level(a) ?? 0) ? b : a ?? b;
}
