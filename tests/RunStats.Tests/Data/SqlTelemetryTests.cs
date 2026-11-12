using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenTelemetry.Trace;

namespace RunStats.Tests.Data;

[Collection("Postgres")]
public class SqlTelemetryTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Sql_commands_become_spans()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("OTEL_EXPORTER_OTLP_ENDPOINT", "http://localhost:4317")])
            .Build();
        await using var services = new ServiceCollection().AddLogging().AddAppTelemetry(configuration).BuildServiceProvider();
        services.GetRequiredService<TracerProvider>();
        var marker = $"telemetry_{Guid.NewGuid():N}";
        using var spans = new ActivityCollector("Npgsql",
            a => (a.GetTagItem("db.query.text") as string)?.Contains(marker) == true);

        await using (var connection = new NpgsqlConnection(postgres.AdminConnectionString))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = new NpgsqlCommand($"SELECT 1 AS {marker}", connection);
            await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        }

        Assert.Single(spans.Stopped);
    }
}
