namespace RunStats.Data;

public class RunLap
{
    public long RunId { get; set; }
    public int LapIndex { get; set; }
    public DateTime StartUtc { get; set; }
    public string? IntensityType { get; set; }

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

    public double? AveragePowerWatts { get; set; }
    public double? MaxPowerWatts { get; set; }
    public double? NormalizedPowerWatts { get; set; }

    public double? ElevationGainMeters { get; set; }
    public double? ElevationLossMeters { get; set; }
    public double? Calories { get; set; }
}
