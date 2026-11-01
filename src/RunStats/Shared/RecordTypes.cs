namespace RunStats.Shared;

// Garmin's personal record types for running. The values are times in seconds, except the longest run (metres).
public static class RecordTypes
{
    public const int LongestRun = 7;

    static readonly Dictionary<int, string> Names = new()
    {
        [1] = "1 km",
        [2] = "1 mile",
        [3] = "5 km",
        [4] = "10 km",
        [5] = "Half marathon",
        [6] = "Marathon",
        [LongestRun] = "Longest run"
    };

    // The timed records and their distance in metres.
    public static readonly IReadOnlyDictionary<int, double> Distances = new Dictionary<int, double>
    {
        [1] = 1000,
        [2] = 1609.344,
        [3] = 5000,
        [4] = 10000,
        [5] = 21097.5,
        [6] = 42195
    };

    public static IReadOnlyCollection<int> Known => Names.Keys;

    public static string? Name(int typeId) => Names.GetValueOrDefault(typeId);

    public static string Value(int typeId, double value) =>
        typeId == LongestRun ? $"{Format.Km(value)} km" : Format.Duration(value);
}
