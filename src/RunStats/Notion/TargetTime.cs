using System.Globalization;

namespace RunStats.Notion;

public static class TargetTime
{
    const int MaxFirstPartDigits = 4;

    // The same forms as the pace calculator: "1:45:30", "45:30", or "45" for minutes.
    public static double? ParseSeconds(string text)
    {
        var parts = text.Trim().Split(':');
        if (parts.Length > 3 || parts[0].Length is 0 or > MaxFirstPartDigits || parts.Skip(1).Any(p => p.Length != 2) || !parts.All(p => p.All(char.IsAsciiDigit)))
            return null;
        var numbers = parts.Select(p => int.Parse(p, CultureInfo.InvariantCulture)).ToList();
        if (numbers.Skip(1).Any(n => n >= 60))
            return null;
        var seconds = numbers.Count == 1 ? numbers[0] * 60 : numbers.Aggregate(0, (total, part) => total * 60 + part);
        return seconds > 0 ? seconds : null;
    }
}
