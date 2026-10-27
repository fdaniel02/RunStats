using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using RunStats.Data;
using RunStats.Tests.Data;

namespace RunStats.Tests;

// The whole app with its production configuration, against a real SQL Server.
[Collection("SqlServer")]
public sealed class StartupTests : IDisposable
{
    readonly WebApplicationFactory<Program> factory;

    readonly SqlServerFixture sqlServer;
    readonly string databaseName = SqlServerFixture.NewDatabaseName();

    public StartupTests(SqlServerFixture sqlServer)
    {
        this.sqlServer = sqlServer;
        var connectionString = sqlServer.ConnectionString(databaseName);
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseEnvironment("Production")
            .UseSetting("ConnectionStrings:Default", connectionString));
    }

    [Theory]
    [InlineData("/", "Dashboard")]
    [InlineData("/runs", "Runs")]
    [InlineData("/heatmap", "Heatmap")]
    public async Task App_starts_and_serves_the_pages(string path, string heading)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Matches($"<h1[^>]*>{heading}</h1>", html);
        Assert.Contains("_framework/blazor.web", html);
    }

    [Fact]
    public async Task Unknown_path_shows_the_not_found_page()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/no-such-page", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("<h1>Not found</h1>", html);
    }

    [Fact]
    public void Logs_are_json_and_skip_ef_core_sql_commands()
    {
        var console = factory.Services.GetRequiredService<IOptionsMonitor<ConsoleLoggerOptions>>().CurrentValue;
        var loggers = factory.Services.GetRequiredService<ILoggerFactory>();

        Assert.Equal(ConsoleFormatterNames.Json, console.FormatterName);
        Assert.False(loggers.CreateLogger("Microsoft.EntityFrameworkCore.Database.Command").IsEnabled(LogLevel.Information));
        Assert.True(loggers.CreateLogger("Microsoft.EntityFrameworkCore.Migrations").IsEnabled(LogLevel.Information));
    }

    [Fact]
    public async Task Pages_retry_database_outages_but_the_migration_does_not()
    {
        var pages = factory.Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var migration = factory.Services.GetRequiredKeyedService<IDbContextFactory<AppDbContext>>(DatabaseMigrator.DbFactoryKey);

        await using var pageDb = await pages.CreateDbContextAsync(TestContext.Current.CancellationToken);
        await using var migrationDb = await migration.CreateDbContextAsync(TestContext.Current.CancellationToken);
        Assert.True(pageDb.Database.CreateExecutionStrategy().RetriesOnFailure);
        Assert.False(migrationDb.Database.CreateExecutionStrategy().RetriesOnFailure);
    }

    [Theory]
    [InlineData("/healthz/live")]
    [InlineData("/healthz/ready")]
    public async Task Health_checks_report_healthy(string path)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Without_the_database_only_readiness_fails()
    {
        using var client = factory.CreateClient();
        await DropDatabaseAsync();

        var live = await client.GetAsync("/healthz/live", TestContext.Current.CancellationToken);
        var ready = await client.GetAsync("/healthz/ready", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Equal("Unhealthy", await ready.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("https", true)]
    [InlineData(null, false)]
    public async Task Hsts_is_sent_when_the_proxy_received_https(string? forwardedProto, bool expectHsts)
    {
        using var app = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
            s.AddSingleton<IStartupFilter>(new RemoteIpStartupFilter(IPAddress.Parse("10.42.0.17")))));
        using var client = app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/healthz/live");
        // UseHsts skips localhost.
        request.Headers.Host = "workouttracker.example";
        if (forwardedProto is not null)
            request.Headers.Add("X-Forwarded-Proto", forwardedProto);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(expectHsts, response.Headers.Contains("Strict-Transport-Security"));
    }

    // TestServer has no remote address; ForwardedHeaders only trusts loopback proxies unless told otherwise.
    sealed class RemoteIpStartupFilter(IPAddress proxy) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = proxy;
                return nextMiddleware(context);
            });
            next(app);
        };
    }

    // The app can't start without its database (it migrates at startup), so the database goes away afterwards.
    async Task DropDatabaseAsync()
    {
        SqlConnection.ClearAllPools();
        await using var connection = new SqlConnection(sqlServer.ConnectionString("master"));
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new SqlCommand(
            $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}];", connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    public void Dispose() => factory.Dispose();
}
