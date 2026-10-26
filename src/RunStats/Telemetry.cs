using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace RunStats;

public static class Telemetry
{
    public const string Name = "RunStats";

    public static readonly ActivitySource ActivitySource = new(Name);

    static readonly string[] BlazorSources = ["Microsoft.AspNetCore.Components", "Microsoft.AspNetCore.Components.Server.Circuits"];
    static readonly string[] BlazorMeters =
        ["Microsoft.AspNetCore.Components", "Microsoft.AspNetCore.Components.Lifecycle", "Microsoft.AspNetCore.Components.Server.Circuits"];

    // Only with a collector to send to: without one, the OTLP exporter keeps failing against localhost:4317.
    public static IServiceCollection AddAppTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
            return services;

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(configuration["OTEL_SERVICE_NAME"] ?? Name))
            .WithTracing(tracing => tracing
                .AddSource(Name)
                .AddSource(BlazorSources)
                .AddAspNetCoreInstrumentation(options =>
                {
                    options.EnableAspNetCoreSignalRSupport = false;
                    options.Filter = context => IsTraced(context.Request.Path);
                })
                .AddHttpClientInstrumentation()
                .AddSqlClientInstrumentation()
                .AddProcessor<SpanNames>())
            .WithMetrics(metrics => metrics
                .AddMeter(Name)
                .AddMeter(BlazorMeters)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddSqlClientInstrumentation())
            .WithLogging()
            .UseOtlpExporter();
        return services;
    }

    // Static files, Blazor's connection requests (/_blazor/negotiate, the WebSocket) and health probes are plumbing.
    // Nothing that is traced runs as their child: the Blazor event and SQL spans start their own traces. (The health
    // check's own SQL span is dropped with its request, which is what we want.)
    public static bool IsTraced(PathString path) =>
        !Path.HasExtension(path.Value) && !path.StartsWithSegments("/_blazor") && !path.StartsWithSegments("/healthz");
}
