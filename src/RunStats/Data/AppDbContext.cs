using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace RunStats.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<Run> Runs => Set<Run>();
    public DbSet<RunRawData> RunRawData => Set<RunRawData>();
    public DbSet<RunLap> RunLaps => Set<RunLap>();
    public DbSet<RunSample> RunSamples => Set<RunSample>();
    public DbSet<RunTrack> RunTracks => Set<RunTrack>();
    public DbSet<PersonalRecord> PersonalRecords => Set<PersonalRecord>();
    public DbSet<SyncRun> SyncRuns => Set<SyncRun>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Run>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.TypeKey).HasMaxLength(50).IsUnicode(false);
            e.HasIndex(x => x.StartUtc);
            e.HasOne(x => x.RawData).WithOne().HasForeignKey<RunRawData>(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Laps).WithOne().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Samples).WithOne().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Track).WithOne().HasForeignKey<RunTrack>(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<RunRawData>(e => e.HasKey(x => x.RunId));
        modelBuilder.Entity<RunLap>(e =>
        {
            e.HasKey(x => new { x.RunId, x.LapIndex });
            e.Property(x => x.IntensityType).HasMaxLength(50).IsUnicode(false);
        });
        modelBuilder.Entity<RunSample>(e => e.HasKey(x => new { x.RunId, x.OffsetSeconds }));
        modelBuilder.Entity<RunTrack>(e =>
        {
            e.HasKey(x => x.RunId);
            e.Property(x => x.EncodedPolyline).IsUnicode(false);
        });
        modelBuilder.Entity<PersonalRecord>(e =>
        {
            e.HasKey(x => x.TypeId);
            e.Property(x => x.TypeId).ValueGeneratedNever();
        });
        modelBuilder.Entity<SyncRun>(e =>
        {
            e.Property(x => x.Trigger).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.Error).HasMaxLength(2000);
            e.HasIndex(x => x.StartedAtUtc);
        });

        // SQL Server doesn't store DateTimeKind; without this, values read back are Unspecified and
        // ToLocalTime() would treat them as server-local time.
        var utc = new ValueConverter<DateTime, DateTime>(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        var utcProperties = modelBuilder.Model.GetEntityTypes()
            .SelectMany(t => t.GetProperties())
            .Where(p => (p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?)) && p.Name.EndsWith("Utc", StringComparison.Ordinal));
        foreach (var property in utcProperties)
            property.SetValueConverter(utc);
    }
}
