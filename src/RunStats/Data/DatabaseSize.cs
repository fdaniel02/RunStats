using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;

namespace RunStats.Data;

// Bytes of the whole database ("data") and of each table. PostgreSQL's write-ahead log belongs to the server, not to
// one database, so there is no "log" any more.
public sealed record DatabaseSize(IReadOnlyDictionary<string, long> Files, IReadOnlyDictionary<string, long> Tables);

// For the database size alerts: the server's volume holds 10 GiB.
public class DatabaseSizeMetrics
{
    DatabaseSize? latest;

    public DatabaseSizeMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(Telemetry.Name);
        meter.CreateObservableGauge("runstats.db.size", () => Measurements(latest?.Files, "runstats.db.file"),
            "By", "Size of the database on disk");
        meter.CreateObservableGauge("runstats.db.table.size", () => Measurements(latest?.Tables, "runstats.db.table"),
            "By", "Space reserved by each table, its indexes included");
    }

    public void Update(DatabaseSize size) => latest = size;

    static IEnumerable<Measurement<long>> Measurements(IReadOnlyDictionary<string, long>? values, string tag) =>
        values?.Select(v => new Measurement<long>(v.Value, new KeyValuePair<string, object?>(tag, v.Key))) ?? [];

    sealed record Row(string Name, long Bytes);

    public static async Task<DatabaseSize> ReadAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var data = await db.Database.SqlQuery<long>($"""SELECT pg_database_size(current_database()) AS "Value" """)
            .SingleAsync(cancellationToken);
        // pg_total_relation_size includes the indexes and the TOAST table, where long values (the raw JSON) go.
        var tables = await db.Database.SqlQuery<Row>($"""
            SELECT c.relname AS "Name", pg_total_relation_size(c.oid) AS "Bytes"
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relkind = 'r' AND n.nspname = 'public'
            """).ToListAsync(cancellationToken);
        return new DatabaseSize(new Dictionary<string, long> { ["data"] = data }, tables.ToDictionary(t => t.Name, t => t.Bytes));
    }
}

// Reads the sizes at start and after each sync, the only writer, instead of on a timer: on an idle app every read would
// open a new connection for nothing.
public class DatabaseSizeMonitor(
    IDbContextFactory<AppDbContext> dbFactory, DatabaseSizeMetrics metrics, ILogger<DatabaseSizeMonitor> logger)
    : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => RefreshAsync(stoppingToken);

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            metrics.Update(await DatabaseSizeMetrics.ReadAsync(db, cancellationToken));
        }
        // A read cancelled by the stop can fail with a database error instead of an OperationCanceledException; left to
        // escape, it would be logged as a failed background service or fail the sync.
        catch (Exception e)
        {
            // The last sizes stay reported; a database outage shows up in the health check, not here.
            if (!cancellationToken.IsCancellationRequested)
                logger.DatabaseSizeFailed(e);
        }
    }
}
