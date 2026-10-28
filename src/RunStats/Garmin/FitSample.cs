namespace RunStats.Garmin;

// One second of a run as the gateway decodes it from a FIT file (POST /fit/samples).
public sealed record FitSample(
    int OffsetSeconds,
    int? LatitudeSemicircles,
    int? LongitudeSemicircles,
    double? DistanceMeters,
    double? SpeedMps,
    double? AltitudeMeters,
    int? HeartRate,
    double? CadenceSpm,
    double? StepLengthMm,
    double? GroundContactTimeMs,
    double? VerticalOscillationMm,
    double? VerticalRatioPercent,
    double? GroundContactBalanceLeftPercent,
    int? PowerWatts,
    int? TemperatureCelsius);
