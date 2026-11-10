using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace RunStats.Tests;

public class TelemetryTests
{
    static ServiceProvider Services(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();
        return new ServiceCollection().AddLogging().AddAppTelemetry(configuration).BuildServiceProvider();
    }

    [Fact]
    public void Nothing_is_registered_without_a_collector_endpoint()
    {
        using var services = Services();

        Assert.Null(services.GetService<TracerProvider>());
        Assert.Null(services.GetService<MeterProvider>());
    }

    [Fact]
    public void Traces_and_metrics_are_set_up_with_a_collector_endpoint()
    {
        using var services = Services(("OTEL_EXPORTER_OTLP_ENDPOINT", "http://localhost:4317"));

        var tracing = services.GetRequiredService<TracerProvider>();
        Assert.NotNull(services.GetService<MeterProvider>());
        Assert.Equal(Telemetry.Name, ServiceName(tracing));
    }

    [Fact]
    public void Service_name_comes_from_OTEL_SERVICE_NAME()
    {
        using var services = Services(("OTEL_EXPORTER_OTLP_ENDPOINT", "http://localhost:4317"), ("OTEL_SERVICE_NAME", "runstats-test"));

        Assert.Equal("runstats-test", ServiceName(services.GetRequiredService<TracerProvider>()));
    }

    [Theory]
    [InlineData(null, Telemetry.Name)]
    [InlineData("runstats-test", "runstats-test")]
    public void Instance_id_is_the_service_name_so_it_stays_the_same_across_restarts(string? serviceName, string expected)
    {
        (string, string)[] settings = serviceName is null
            ? [("OTEL_EXPORTER_OTLP_ENDPOINT", "http://localhost:4317")]
            : [("OTEL_EXPORTER_OTLP_ENDPOINT", "http://localhost:4317"), ("OTEL_SERVICE_NAME", serviceName)];
        using var services = Services(settings);

        Assert.Equal(expected, Attribute(services.GetRequiredService<TracerProvider>(), "service.instance.id"));
    }

    [Fact]
    public void Blazor_lifecycle_metrics_are_left_out()
    {
        Assert.DoesNotContain("Microsoft.AspNetCore.Components.Lifecycle", Telemetry.BlazorMeters);
        Assert.Contains("Microsoft.AspNetCore.Components.Server.Circuits", Telemetry.BlazorMeters);
    }

    [Fact]
    public void SignalR_spans_and_untraced_requests_are_left_out()
    {
        using var services = Services(("OTEL_EXPORTER_OTLP_ENDPOINT", "http://localhost:4317"));

        var options = services.GetRequiredService<IOptionsMonitor<AspNetCoreTraceInstrumentationOptions>>().Get(Options.DefaultName);

        Assert.False(options.EnableAspNetCoreSignalRSupport);
        Assert.False(options.Filter!(new DefaultHttpContext { Request = { Path = "/app.css" } }));
        Assert.True(options.Filter(new DefaultHttpContext { Request = { Path = "/runs" } }));
    }

    [Theory]
    [InlineData("/", true)]
    [InlineData("/runs", true)]
    [InlineData("/heatmap", true)]
    [InlineData("/app.o8n0hzhsih.css", false)]
    [InlineData("/_framework/blazor.web.cd06xom9c5.js", false)]
    [InlineData("/favicon.png", false)]
    [InlineData("/_blazor", false)]
    [InlineData("/_blazor/negotiate", false)]
    [InlineData("/_blazor/initializers/", false)]
    [InlineData("/_blazorish", true)]
    [InlineData("/healthz/live", false)]
    [InlineData("/healthz/ready", false)]
    public void Static_files_and_Blazor_connection_requests_are_not_traced(string path, bool traced) =>
        Assert.Equal(traced, Telemetry.IsTraced(path));

    static object? ServiceName(TracerProvider tracing) => Attribute(tracing, "service.name");

    static object? Attribute(TracerProvider tracing, string key) =>
        tracing.GetResource().Attributes.Single(a => a.Key == key).Value;
}
