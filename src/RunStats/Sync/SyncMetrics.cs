using System.Diagnostics.Metrics;
using RunStats.Data;

namespace RunStats.Sync;

public class SyncMetrics
{
    readonly Counter<long> syncs;
    readonly Histogram<double> duration;
    readonly Counter<long> runs;
    long? lastSuccessUnixSeconds;

    public SyncMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(Telemetry.Name);
        syncs = meter.CreateCounter<long>("runstats.syncs", "{sync}", "Syncs with Garmin by trigger and outcome");
        duration = meter.CreateHistogram<double>("runstats.sync.duration", "s", "Duration of syncs with Garmin");
        runs = meter.CreateCounter<long>("runstats.sync.runs", "{run}", "Runs added, updated or deleted by syncs");
        // For the "no successful sync for a day" alert; nothing is reported before the first successful sync.
        meter.CreateObservableGauge("runstats.sync.last_success", () =>
            lastSuccessUnixSeconds is { } value ? [new Measurement<long>(value)] : Array.Empty<Measurement<long>>(),
            "s", "Time of the last successful sync (Unix time)");
    }

    public void LastSuccessAt(DateTime utc) => lastSuccessUnixSeconds = new DateTimeOffset(utc, TimeSpan.Zero).ToUnixTimeSeconds();

    public void SyncFinished(SyncTrigger trigger, SyncStatus outcome, TimeSpan elapsed)
    {
        KeyValuePair<string, object?>[] tags =
        [
            new("runstats.sync.trigger", trigger.ToString().ToLowerInvariant()),
            new("runstats.sync.outcome", outcome.ToString().ToLowerInvariant())
        ];
        syncs.Add(1, tags);
        duration.Record(elapsed.TotalSeconds, tags);
    }

    public void RunsChanged(string change, int count)
    {
        if (count > 0)
            runs.Add(count, new KeyValuePair<string, object?>("runstats.sync.change", change));
    }
}
