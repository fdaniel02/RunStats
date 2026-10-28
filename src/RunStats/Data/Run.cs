namespace RunStats.Data;

// Units are the ones Garmin uses; the column names say which.
public class Run : RunMetrics
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

    public short? MinHeartRate { get; set; }
    public double? MinElevationMeters { get; set; }
    public double? MaxElevationMeters { get; set; }

    public double? AerobicTrainingEffect { get; set; }
    public double? AnaerobicTrainingEffect { get; set; }
    // Garmin's name for the run's main training benefit, e.g. TEMPO or VO2MAX.
    public string? TrainingEffectLabel { get; set; }
    public double? TrainingLoad { get; set; }
    public double? Vo2Max { get; set; }
    public int? Steps { get; set; }
    public double? AverageTemperatureCelsius { get; set; }
    // Not Garmin's: calculated from the samples (PaceVariation).
    public double? PaceVariationPercent { get; set; }

    // From Garmin's weather record: the nearest weather station's reading around the start. Empty for indoor runs.
    public double? WeatherTemperatureCelsius { get; set; }
    public double? WeatherFeelsLikeCelsius { get; set; }
    public double? WeatherDewPointCelsius { get; set; }
    public int? WeatherHumidityPercent { get; set; }
    public double? WeatherWindSpeedKmh { get; set; }
    // A compass point, e.g. "SSW".
    public string? WeatherWindDirection { get; set; }
    public string? WeatherStation { get; set; }

    public double? StartLatitude { get; set; }
    public double? StartLongitude { get; set; }
    public double? EndLatitude { get; set; }
    public double? EndLongitude { get; set; }

    // Raised when the processing changes, so stored runs can be processed again from their raw data.
    public int ProcessorVersion { get; set; }
    // Why the raw data couldn't be processed; the run is tried again when the processing changes.
    public string? ProcessingError { get; set; }

    public RunRawData? RawData { get; set; }
    public List<RunLap> Laps { get; set; } = [];
    public List<RunSample> Samples { get; set; } = [];
    public List<RunPaceBand> PaceBands { get; set; } = [];
    public RunTrack? Track { get; set; }
}
