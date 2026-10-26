using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;

namespace RunStats.Tests.Data;

[Collection("SqlServer")]
public class SqlTelemetryTests(SqlServerFixture sqlServer)
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
        using var spans = new ActivityCollector("OpenTelemetry.Instrumentation.SqlClient",
            a => (a.GetTagItem("db.query.text") as string)?.Contains(marker) == true);

        await using (var connection = new SqlConnection(sqlServer.ConnectionString("master")))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = new SqlCommand($"SELECT 1 AS {marker}", connection);
            await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        }

        Assert.Single(spans.Stopped);
    }
}
