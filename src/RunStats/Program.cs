using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using RunStats;
using RunStats.Components;
using RunStats.Data;
using RunStats.Features.Dashboard;
using RunStats.Features.Heatmap;
using RunStats.Features.Runs;
using RunStats.Features.Trends;
using RunStats.Features.Warnings;
using RunStats.Garmin;
using RunStats.Processing;
using RunStats.Sync;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDbContextFactory<AppDbContext>((services, o) =>
    o.UseAppDatabase(services.GetRequiredService<IConfiguration>(), retryOnFailure: true));
// Without EF's retries: StartupRetry already retries the migration, and the two loops would multiply.
builder.Services.AddKeyedSingleton<IDbContextFactory<AppDbContext>>(DatabaseMigrator.DbFactoryKey, (services, _) =>
    new PooledDbContextFactory<AppDbContext>(new DbContextOptionsBuilder<AppDbContext>()
        .UseAppDatabase(services.GetRequiredService<IConfiguration>(), retryOnFailure: false).Options));

// In the database, not the container: keys lost on a restart make every browser's antiforgery cookie undecryptable.
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<AppDbContext>()
    .SetApplicationName("RunStats");

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // The proxy (Traefik) has no fixed IP, and the pod is only reachable through the cluster network.
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();
builder.Services.AddAppTelemetry(builder.Configuration);

builder.Services.AddSingleton<DatabaseMigrator>();
builder.Services.AddTransient<IDashboardQueries, DashboardQueries>();
builder.Services.AddTransient<IHeatmapQueries, HeatmapQueries>();
builder.Services.AddTransient<IRunListQueries, RunListQueries>();
builder.Services.AddTransient<IRunDetailsQueries, RunDetailsQueries>();
builder.Services.AddOptions<WarningSettings>().BindConfiguration(WarningSettings.Section).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddTransient<IWarningQueries, WarningQueries>();
builder.Services.AddTransient<ITrendsQueries, TrendsQueries>();
builder.Services.AddTransient<IFormQueries, FormQueries>();
builder.Services.AddHttpClient<IGarminGateway, GarminGatewayClient>((services, client) =>
    client.BaseAddress = new Uri(services.GetRequiredService<IConfiguration>()["GarminGateway:BaseUrl"]
        ?? throw new InvalidOperationException("Setting 'GarminGateway:BaseUrl' is missing (e.g. 'http://garmin-gateway:8080/').")));
builder.Services.AddSingleton<SyncMetrics>();
builder.Services.AddSingleton<SyncCoordinator>();
builder.Services.AddTransient<ISyncStatusQueries, SyncStatusQueries>();
builder.Services.AddTransient<RunProcessing>();
builder.Services.AddTransient<PersonalRecordUpdate>();
builder.Services.AddTransient<SyncJob>();
builder.Services.AddHostedService<SyncWorker>();
builder.Services.AddSingleton<DatabaseSizeMetrics>();
builder.Services.AddHostedService<DatabaseSizeMonitor>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton(TimeProvider.System);
// Resolved after Build(), so a bad setting is logged like any other startup error.
builder.Services.AddSingleton(services =>
{
    var timeZoneId = builder.Configuration["TimeZone"]
        ?? throw new InvalidOperationException("Setting 'TimeZone' is missing (an IANA id, e.g. 'Europe/Amsterdam').");
    return new Clock(TimeZoneInfo.FindSystemTimeZoneById(timeZoneId), services.GetRequiredService<TimeProvider>());
});

// Disposing the app on exit flushes the console logger's queue, so the last error is written before the process ends.
await using var app = builder.Build();

try
{
    app.Services.GetRequiredService<Clock>();
    await app.Services.GetRequiredService<DatabaseMigrator>().MigrateAsync();
    await app.Services.GetRequiredService<SyncCoordinator>().InitializeAsync();

    app.UseForwardedHeaders();

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error", createScopeForErrors: true);
        app.UseHsts();
    }
    app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

    app.UseAntiforgery();

    // Liveness without the database check: a database outage must not make Kubernetes restart the pod.
    app.MapHealthChecks("/healthz/live", new HealthCheckOptions { Predicate = _ => false });
    app.MapHealthChecks("/healthz/ready");
    app.MapSyncEndpoints();

    app.MapStaticAssets();
    app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode();

    await app.RunAsync();
    return 0;
}
// dotnet ef and WebApplicationFactory stop the app with HostAbortedException; it isn't an error.
catch (Exception ex) when (ex is not HostAbortedException)
{
    // Logged here, not left to the runtime: an unhandled exception goes to stderr as multi-line text, not JSON.
    app.Logger.ApplicationFailed(ex);
    return 1;
}
