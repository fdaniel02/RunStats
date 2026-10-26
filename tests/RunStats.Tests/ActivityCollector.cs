using System.Collections.Concurrent;
using System.Diagnostics;

namespace RunStats.Tests;

// Tests run in parallel and the ActivitySource is static, so callers filter on something only their test produces.
public sealed class ActivityCollector : IDisposable
{
    readonly ConcurrentQueue<Activity> stopped = new();
    readonly ActivityListener listener;

    public ActivityCollector(Func<Activity, bool> filter) : this(Telemetry.Name, filter) { }

    public ActivityCollector(string sourceName, Func<Activity, bool> filter)
    {
        listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == sourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (filter(activity))
                    stopped.Enqueue(activity);
            }
        };
        ActivitySource.AddActivityListener(listener);
    }

    public IReadOnlyList<Activity> Stopped => [.. stopped];

    public void Dispose() => listener.Dispose();
}
