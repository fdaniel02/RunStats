using System.Globalization;
using System.Text.Json;
using RunStats.Data;

namespace RunStats.Processing;

public static class RacePredictionMapper
{
    static readonly (string Field, int TypeId)[] Fields =
    [
        ("time5K", 3),
        ("time10K", 4),
        ("timeHalfMarathon", 5),
        ("timeMarathon", 6)
    ];

    public static List<RacePrediction> FromJson(string? json)
    {
        if (json is null)
            return [];
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new FormatException("Garmin's race predictions aren't an object.");
        if (!root.TryGetProperty("calendarDate", out var date) || date.ValueKind != JsonValueKind.String
            || !DateOnly.TryParseExact(date.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var predictedOn))
            return [];

        var predictions = new List<RacePrediction>();
        foreach (var (field, typeId) in Fields)
        {
            if (root.TryGetProperty(field, out var time) && time.ValueKind == JsonValueKind.Number && time.GetDouble() > 0)
                predictions.Add(new RacePrediction { TypeId = typeId, Seconds = time.GetDouble(), PredictedOn = predictedOn });
        }
        return predictions;
    }
}
