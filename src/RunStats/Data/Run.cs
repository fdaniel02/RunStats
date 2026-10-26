namespace RunStats.Data;

// Units are the ones Garmin uses; the column names say which.
public class Run
{
    // Garmin's activity id.
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string TypeKey { get; set; } = "";
    public int TypeId { get; set; }
    public int ParentTypeId { get; set; }
    public bool IsRace { get; set; }
    public DateTime StartUtc { get; set; }
    // Garmin's local time at the place of the run, which can differ from the app's TimeZone setting.
    public DateTime StartLocal { get; set; }

    public double DistanceMeters { get; set; }
    public double DurationSeconds { get; set; }
    public double? MovingDurationSeconds { get; set; }
    public double? ElapsedDurationSeconds { get; set; }

    public double? AverageSpeedMps { get; set; }
    public double? AverageMovingSpeedMps { get; set; }
    public double? GradeAdjustedSpeedMps { get; set; }
    public double? MaxSpeedMps { get; set; }

    public short? MinHeartRate { get; set; }
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
    public double? MinElevationMeters { get; set; }
    public double? MaxElevationMeters { get; set; }

    public double? AerobicTrainingEffect { get; set; }
    public double? AnaerobicTrainingEffect { get; set; }
    public double? TrainingLoad { get; set; }
    public double? Vo2Max { get; set; }
    public double? Calories { get; set; }
    public int? Steps { get; set; }
    public double? AverageTemperatureCelsius { get; set; }

    public double? StartLatitude { get; set; }
    public double? StartLongitude { get; set; }
    public double? EndLatitude { get; set; }
    public double? EndLongitude { get; set; }

    // Raised when the processing changes, so stored runs can be processed again from their raw data.
    public int ProcessorVersion { get; set; }

    public RunRawData? RawData { get; set; }
    public List<RunLap> Laps { get; set; } = [];
    public List<RunSample> Samples { get; set; } = [];
    public RunTrack? Track { get; set; }
}
