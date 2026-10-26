using System.Diagnostics;

namespace RunStats.Tests;

public class SpanNamesTests
{
    static readonly ActivitySource Components = new("Microsoft.AspNetCore.Components");
    static readonly ActivitySource Circuits = new("Microsoft.AspNetCore.Components.Server.Circuits");
    static readonly ActivitySource Other = new("RunStats.Tests.SpanNames");

    static string Readable(ActivitySource source, string name, params (string Key, string Value)[] tags)
    {
        using var collector = new ActivityCollector(source.Name, _ => false);
        using var activity = source.StartActivity(name)!;
        foreach (var (key, value) in tags)
            activity.SetTag(key, value);
        return SpanNames.Readable(activity);
    }

    [Fact]
    public void Event_names_the_event_component_and_method()
    {
        var name = Readable(Components, "Event onclick -> RunStats.Components.Layout.SyncButton.SyncAsync",
            ("aspnetcore.components.type", "RunStats.Components.Layout.SyncButton"),
            ("code.function.name", "SyncAsync"),
            ("aspnetcore.components.attribute.name", "onclick"));

        Assert.Equal("Click SyncButton.SyncAsync", name);
    }

    [Fact]
    public void Event_without_an_attribute_name_is_called_Event()
    {
        var name = Readable(Components, "Event",
            ("aspnetcore.components.type", "RunStats.Features.Runs.Runs"),
            ("code.function.name", "ApplyFiltersAsync"));

        Assert.Equal("Event Runs.ApplyFiltersAsync", name);
    }

    [Fact]
    public void Route_names_the_route_and_component_without_constraints()
    {
        var name = Readable(Components, "Route /runs/{Id:long} -> RunStats.Features.Runs.RunDetails",
            ("aspnetcore.components.type", "RunStats.Features.Runs.RunDetails"),
            ("aspnetcore.components.route", "/runs/{Id:long}"));

        Assert.Equal("Route /runs/{Id} -> RunDetails", name);
    }

    [Fact]
    public void Circuit_drops_the_id()
    {
        var name = Readable(Circuits, "Circuit rsVrOiOtgCt77uo5GpF1soy_uMGPmxN6_dYPNski0bI",
            ("aspnetcore.components.circuit.id", "rsVrOiOtgCt77uo5GpF1soy_uMGPmxN6_dYPNski0bI"));

        Assert.Equal("Circuit", name);
    }

    [Theory]
    [InlineData("GET /api/sync/{Id:long}", "GET /api/sync/{Id}")]
    [InlineData("GET /files/{*path}", "GET /files/{*path}")]
    [InlineData("GET /{page?}", "GET /{page}")]
    [InlineData("Sync", "Sync")]
    public void Other_spans_only_lose_route_constraints(string original, string expected) =>
        Assert.Equal(expected, Readable(Other, original));

    [Fact]
    public void Component_span_without_known_tags_keeps_its_name() =>
        Assert.Equal("Something", Readable(Components, "Something"));

    [Fact]
    public void Processor_renames_the_span_when_it_ends()
    {
        using var collector = new ActivityCollector(Circuits.Name, _ => false);
        using var activity = Circuits.StartActivity("Circuit abc")!;

        new SpanNames().OnEnd(activity);

        Assert.Equal("Circuit", activity.DisplayName);
    }
}
