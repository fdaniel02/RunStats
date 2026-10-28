namespace RunStats.Data;

// What Garmin reports both for a whole run and for each lap. Not an entity: each table gets its own columns.
public abstract class RunMetrics
{
    public double DistanceMeters { get; set; }
    public double DurationSeconds { get; set; }
    public double? MovingDurationSeconds { get; set; }
    public double? ElapsedDurationSeconds { get; set; }

    public double? AverageSpeedMps { get; set; }
    public double? AverageMovingSpeedMps { get; set; }
    public double? GradeAdjustedSpeedMps { get; set; }
    public double? MaxSpeedMps { get; set; }

    public short? AverageHeartRate { get; set; }
    public short? MaxHeartRate { get; set; }

    public double? AverageCadenceSpm { get; set; }
    public double? MaxCadenceSpm { get; set; }
    public double? StrideLengthCm { get; set; }
    public double? GroundContactTimeMs { get; set; }
    public double? VerticalOscillationCm { get; set; }
    public double? VerticalRatioPercent { get; set; }
    public double? GroundContactBalanceLeftPercent { get; set; }

    public double? AveragePowerWatts { get; set; }
    public double? MaxPowerWatts { get; set; }
    public double? NormalizedPowerWatts { get; set; }

    public double? ElevationGainMeters { get; set; }
    public double? ElevationLossMeters { get; set; }
    public double? Calories { get; set; }
}
