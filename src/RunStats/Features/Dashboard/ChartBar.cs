namespace RunStats.Features.Dashboard;

// One bar of a PeriodChart: Name for the tooltip and table, Axis the label under the bar (none for most weeks of a year).
public sealed record ChartBar(string Name, string? Axis, double DistanceMeters, double DurationSeconds, bool IsCurrent);
