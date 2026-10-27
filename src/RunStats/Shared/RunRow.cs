namespace RunStats.Shared;

// A run as the tables show it; Date is Garmin's local date of the run.
public sealed record RunRow(long Id, DateOnly Date, string TypeKey, string Name, double DistanceMeters, double DurationSeconds);
