using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;

namespace RunStats.Data;

// Bytes by file ("data", "log") and by table.
public sealed record DatabaseSize(IReadOnlyDictionary<string, long> Files, IReadOnlyDictionary<string, long> Tables);

// For the database size alerts: SQL Server Express stops at 10 GB of data per database.
public class DatabaseSizeMetrics
{
    DatabaseSize? latest;

    public DatabaseSizeMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(Telemetry.Name);
        meter.CreateObservableGauge("runstats.db.size", () => Measurements(latest?.Files, "runstats.db.file"),
            "By", "Allocated size of the database's data and log files");
        meter.CreateObservableGauge("runstats.db.table.size", () => Measurements(latest?.Tables, "runstats.db.table"),
            "By", "Space reserved by each table, its indexes included");
    }

    public void Update(DatabaseSize size) => latest = size;

    static IEnumerable<Measurement<long>> Measurements(IReadOnlyDictionary<string, long>? values, string tag) =>
        values?.Select(v => new Measurement<long>(v.Value, new KeyValuePair<string, object?>(tag, v.Key))) ?? [];

    sealed record Row(string Name, long Bytes);

    // Catalog views only: the app's login can read them, while the DMVs would need VIEW DATABASE STATE.
    public static async Task<DatabaseSize> ReadAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var files = await db.Database.SqlQuery<Row>($"""
            SELECT CASE type_desc WHEN 'ROWS' THEN 'data' ELSE LOWER(type_desc) END AS Name, SUM(CAST(size AS bigint)) * 8192 AS Bytes
            FROM sys.database_files
            GROUP BY type_desc
            """).ToListAsync(cancellationToken);
        // LOB pages belong to the partition, in-row and row-overflow pages to its heap or B-tree.
        var tables = await db.Database.SqlQuery<Row>($"""
            SELECT t.name AS Name, SUM(a.total_pages) * 8192 AS Bytes
            FROM sys.tables t
            JOIN sys.partitions p ON p.object_id = t.object_id
            JOIN sys.allocation_units a ON a.container_id = CASE WHEN a.type IN (1, 3) THEN p.hobt_id ELSE p.partition_id END
            WHERE t.is_ms_shipped = 0
            GROUP BY t.name
            """).ToListAsync(cancellationToken);
        return new DatabaseSize(files.ToDictionary(f => f.Name, f => f.Bytes), tables.ToDictionary(t => t.Name, t => t.Bytes));
    }
}

// Reads the sizes now and then rather than on every metrics export, which happens every minute.
public class DatabaseSizeMonitor(
    IDbContextFactory<AppDbContext> dbFactory, DatabaseSizeMetrics metrics, TimeProvider time, ILogger<DatabaseSizeMonitor> logger)
    : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var db = await dbFactory.CreateDbContextAsync(stoppingToken);
                metrics.Update(await DatabaseSizeMetrics.ReadAsync(db, stoppingToken));
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            {
                // The last sizes stay reported; a database outage shows up in the health check, not here.
                logger.DatabaseSizeFailed(e);
            }
            // SQL Server reports a read cancelled by the stop as a SqlException, not an OperationCanceledException; left to
            // escape, it would be logged as a failed background service.
            catch (Exception) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            await Task.Delay(Interval, time, stoppingToken);
        }
    }
}
