using System.Globalization;
using System.Text.Json;
using RunStats.Data;
using RunStats.Garmin;

namespace RunStats.Processing;

// Turns a run's raw Garmin data into the typed columns, laps and samples.
public static class RunProcessor
{
    // Raise when the processing changes: every stored run is then processed again at the next sync.
    public const int Version = 5;

    const int MaxIntensityTypeLength = 50;
    const int MaxTrainingEffectLabelLength = 50;
    const int MaxWindDirectionLength = 3;
    const int MaxStationLength = 100;
    // Garmin's weather record has °F and wind in knots: 7 shows as 13 km/h in Garmin Connect.
    const double KmhPerKnot = 1.852;

    public static void ApplySummary(Run run, RunRawData raw)
    {
        using var detail = JsonDocument.Parse(raw.DetailJson);
        if (!detail.RootElement.TryGetProperty("summaryDTO", out var summary) || summary.ValueKind != JsonValueKind.Object)
            throw new FormatException("The activity detail has no summary.");

        ApplyMetrics(run, summary);
        run.MinHeartRate = HeartRate(summary, "minHR");
        run.MinElevationMeters = Number(summary, "minElevation");
        run.MaxElevationMeters = Number(summary, "maxElevation");
        run.AerobicTrainingEffect = Number(summary, "trainingEffect");
        run.AnaerobicTrainingEffect = Number(summary, "anaerobicTrainingEffect");
        var label = Text(summary, "trainingEffectLabel");
        run.TrainingEffectLabel = label is { Length: > MaxTrainingEffectLabelLength } ? label[..MaxTrainingEffectLabelLength] : label;
        run.TrainingLoad = Number(summary, "activityTrainingLoad");
        run.Steps = Number(summary, "steps") is { } steps ? (int)Math.Round(steps) : null;
        run.AverageTemperatureCelsius = Number(summary, "averageTemperature");
        run.StartLatitude = Number(summary, "startLatitude");
        run.StartLongitude = Number(summary, "startLongitude");
        run.EndLatitude = Number(summary, "endLatitude");
        run.EndLongitude = Number(summary, "endLongitude");

        // Only the activity list has VO2max.
        using var listEntry = JsonDocument.Parse(raw.ListJson);
        run.Vo2Max = Number(listEntry.RootElement, "vO2MaxValue");

        ApplyWeather(run, raw.WeatherJson);
    }

    // Indoor runs get a record with every value null (or none at all): then all stay empty.
    static void ApplyWeather(Run run, string? weatherJson)
    {
        using var weather = weatherJson is null ? null : JsonDocument.Parse(weatherJson);
        if (weather?.RootElement is not { ValueKind: JsonValueKind.Object } w)
        {
            (run.WeatherTemperatureCelsius, run.WeatherFeelsLikeCelsius, run.WeatherDewPointCelsius) = (null, null, null);
            (run.WeatherHumidityPercent, run.WeatherWindSpeedKmh, run.WeatherWindDirection, run.WeatherStation) = (null, null, null, null);
            return;
        }
        run.WeatherTemperatureCelsius = Celsius(Number(w, "temp"));
        run.WeatherFeelsLikeCelsius = Celsius(Number(w, "apparentTemp"));
        run.WeatherDewPointCelsius = Celsius(Number(w, "dewPoint"));
        run.WeatherHumidityPercent = Number(w, "relativeHumidity") is { } humidity ? (int)Math.Round(humidity) : null;
        run.WeatherWindSpeedKmh = Number(w, "windSpeed") * KmhPerKnot;
        run.WeatherWindDirection = Cut(Text(w, "windDirectionCompassPoint")?.ToUpperInvariant(), MaxWindDirectionLength);
        run.WeatherStation = w.TryGetProperty("weatherStationDTO", out var station) && station.ValueKind == JsonValueKind.Object
            ? Cut(Text(station, "name"), MaxStationLength)
            : null;
    }

    static double? Celsius(double? fahrenheit) => (fahrenheit - 32) * 5 / 9;

    static string? Cut(string? text, int length)
    {
        if (string.IsNullOrEmpty(text))
            return null;
        return text.Length > length ? text[..length] : text;
    }

    public static List<RunLap> Laps(long runId, string? splitsJson)
    {
        if (splitsJson is null)
            return [];
        using var splits = JsonDocument.Parse(splitsJson);
        if (!splits.RootElement.TryGetProperty("lapDTOs", out var laps) || laps.ValueKind != JsonValueKind.Array)
            return [];
        return [.. laps.EnumerateArray().Select(l => Lap(runId, l))];
    }

    static RunLap Lap(long runId, JsonElement lap)
    {
        var intensity = Text(lap, "intensityType");
        var result = new RunLap
        {
            RunId = runId,
            LapIndex = (int)(Number(lap, "lapIndex") ?? throw new FormatException("A lap has no index.")),
            StartUtc = DateTime.Parse(
                Text(lap, "startTimeGMT") ?? throw new FormatException("A lap has no start time."),
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
            IntensityType = intensity is { Length: > MaxIntensityTypeLength } ? intensity[..MaxIntensityTypeLength] : intensity
        };
        ApplyMetrics(result, lap);
        return result;
    }

    // Garmin uses the same field names for the whole run and for a lap.
    static void ApplyMetrics(RunMetrics metrics, JsonElement e)
    {
        metrics.DistanceMeters = Number(e, "distance") ?? metrics.DistanceMeters;
        metrics.DurationSeconds = Number(e, "duration") ?? metrics.DurationSeconds;
        metrics.MovingDurationSeconds = Number(e, "movingDuration");
        metrics.ElapsedDurationSeconds = Number(e, "elapsedDuration");
        metrics.AverageSpeedMps = Number(e, "averageSpeed");
        metrics.AverageMovingSpeedMps = Number(e, "averageMovingSpeed");
        metrics.GradeAdjustedSpeedMps = Number(e, "avgGradeAdjustedSpeed");
        metrics.MaxSpeedMps = Number(e, "maxSpeed");
        metrics.AverageHeartRate = HeartRate(e, "averageHR");
        metrics.MaxHeartRate = HeartRate(e, "maxHR");
        metrics.AverageCadenceSpm = Number(e, "averageRunCadence");
        metrics.MaxCadenceSpm = Number(e, "maxRunCadence");
        metrics.StrideLengthCm = Number(e, "strideLength");
        metrics.GroundContactTimeMs = Number(e, "groundContactTime");
        metrics.VerticalOscillationCm = Number(e, "verticalOscillation");
        metrics.VerticalRatioPercent = Number(e, "verticalRatio");
        metrics.GroundContactBalanceLeftPercent = Number(e, "groundContactBalanceLeft");
        metrics.AveragePowerWatts = Number(e, "averagePower");
        metrics.MaxPowerWatts = Number(e, "maxPower");
        metrics.NormalizedPowerWatts = Number(e, "normalizedPower");
        metrics.ElevationGainMeters = Number(e, "elevationGain");
        metrics.ElevationLossMeters = Number(e, "elevationLoss");
        metrics.Calories = Number(e, "calories");
    }

    public static List<RunSample> Samples(long runId, IEnumerable<FitSample> samples) =>
    [
        .. samples.Select(s => new RunSample
        {
            RunId = runId,
            OffsetSeconds = s.OffsetSeconds,
            LatitudeSemicircles = s.LatitudeSemicircles,
            LongitudeSemicircles = s.LongitudeSemicircles,
            DistanceMeters = (float?)s.DistanceMeters,
            SpeedMps = (float?)s.SpeedMps,
            AltitudeMeters = (float?)s.AltitudeMeters,
            HeartRate = s.HeartRate is >= byte.MinValue and <= byte.MaxValue ? (byte)s.HeartRate : null,
            CadenceSpm = (float?)s.CadenceSpm,
            StepLengthMm = Small(s.StepLengthMm),
            GroundContactTimeMs = (float?)s.GroundContactTimeMs,
            VerticalOscillationMm = (float?)s.VerticalOscillationMm,
            VerticalRatioPercent = (float?)s.VerticalRatioPercent,
            GroundContactBalanceLeftPercent = (float?)s.GroundContactBalanceLeftPercent,
            PowerWatts = Small(s.PowerWatts),
            TemperatureCelsius = Small(s.TemperatureCelsius)
        })
    ];

    // Values outside a smallint are sensor errors, not data worth failing the run for.
    static short? Small(double? value) =>
        value is { } v && Math.Round(v) is var rounded && rounded >= short.MinValue && rounded <= short.MaxValue ? (short)rounded : null;

    static short? HeartRate(JsonElement e, string name) => Small(Number(e, name));

    static double? Number(JsonElement e, string name) =>
        e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

    static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
