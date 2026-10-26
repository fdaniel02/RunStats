using System.Diagnostics;
using System.Text.RegularExpressions;
using OpenTelemetry;

namespace RunStats;

// Blazor names its spans after full type names and the circuit id, e.g.
// "Event onclick -> RunStats.Components.Layout.SyncButton.SyncAsync"; the same details stay in the tags.
public sealed partial class SpanNames : BaseProcessor<Activity>
{
    const string ComponentsSource = "Microsoft.AspNetCore.Components";
    const string CircuitsSource = "Microsoft.AspNetCore.Components.Server.Circuits";

    public override void OnEnd(Activity data) => data.DisplayName = Readable(data);

    public static string Readable(Activity activity) => activity.Source.Name switch
    {
        ComponentsSource when activity.GetTagItem("code.function.name") is string function =>
            $"{EventName(activity.GetTagItem("aspnetcore.components.attribute.name") as string)} {ComponentName(activity)}.{function}",
        ComponentsSource when activity.GetTagItem("aspnetcore.components.route") is string route =>
            $"Route {WithoutConstraints(route)} -> {ComponentName(activity)}",
        CircuitsSource when activity.DisplayName.StartsWith("Circuit ", StringComparison.Ordinal) => "Circuit",
        _ => WithoutConstraints(activity.DisplayName)
    };

    static string ComponentName(Activity activity)
    {
        var type = activity.GetTagItem("aspnetcore.components.type") as string ?? "";
        return type[(type.LastIndexOf('.') + 1)..];
    }

    static string EventName(string? attribute) =>
        attribute is ['o', 'n', var first, .. var rest] ? char.ToUpperInvariant(first) + rest : attribute ?? "Event";

    static string WithoutConstraints(string name) => RouteParameter().Replace(name, "{$1}");

    [GeneratedRegex(@"\{(\**\w+)[^}]*\}")]
    private static partial Regex RouteParameter();
}
