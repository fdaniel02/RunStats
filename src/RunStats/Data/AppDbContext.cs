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
    public DbSet<RunPaceBand> RunPaceBands => Set<RunPaceBand>();
    public DbSet<RunTrack> RunTracks => Set<RunTrack>();
    public DbSet<RunBestEffort> RunBestEfforts => Set<RunBestEffort>();
    public DbSet<RunHeartRateBand> RunHeartRateBands => Set<RunHeartRateBand>();
    public DbSet<YearGoal> YearGoals => Set<YearGoal>();
    public DbSet<PersonalRecord> PersonalRecords => Set<PersonalRecord>();
    public DbSet<RacePrediction> RacePredictions => Set<RacePrediction>();
    public DbSet<UpcomingRace> UpcomingRaces => Set<UpcomingRace>();
    public DbSet<SyncRun> SyncRuns => Set<SyncRun>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Run>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.TypeKey).HasMaxLength(50).IsUnicode(false);
            e.Property(x => x.TrainingEffectLabel).HasMaxLength(50).IsUnicode(false);
            e.Property(x => x.WeatherWindDirection).HasMaxLength(3).IsUnicode(false);
            e.Property(x => x.WeatherStation).HasMaxLength(100);
            e.HasIndex(x => x.StartUtc);
            e.Property(x => x.ProcessingError).HasMaxLength(1000);
            e.HasOne(x => x.RawData).WithOne().HasForeignKey<RunRawData>(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Laps).WithOne().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Samples).WithOne().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.PaceBands).WithOne().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Track).WithOne().HasForeignKey<RunTrack>(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.BestEfforts).WithOne().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.HeartRateBands).WithOne().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<RunRawData>(e => e.HasKey(x => x.RunId));
        modelBuilder.Entity<RunLap>(e =>
        {
            e.HasKey(x => new { x.RunId, x.LapIndex });
            e.Property(x => x.IntensityType).HasMaxLength(50).IsUnicode(false);
        });
        modelBuilder.Entity<RunSample>(e => e.HasKey(x => new { x.RunId, x.OffsetSeconds }));
        modelBuilder.Entity<RunPaceBand>(e => e.HasKey(x => new { x.RunId, x.PaceSecondsPerKm }));
        modelBuilder.Entity<RunBestEffort>(e => e.HasKey(x => new { x.RunId, x.TypeId }));
        modelBuilder.Entity<RunHeartRateBand>(e => e.HasKey(x => new { x.RunId, x.HeartRate }));
        modelBuilder.Entity<YearGoal>(e =>
        {
            e.HasKey(x => x.Year);
            e.Property(x => x.Year).ValueGeneratedNever();
        });
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
        modelBuilder.Entity<RacePrediction>(e =>
        {
            e.HasKey(x => x.TypeId);
            e.Property(x => x.TypeId).ValueGeneratedNever();
        });
        modelBuilder.Entity<UpcomingRace>(e =>
        {
            e.HasKey(x => x.NotionPageId);
            e.Property(x => x.NotionPageId).ValueGeneratedNever();
            e.Property(x => x.Name).HasMaxLength(RunStats.Sync.UpcomingRacesSync.MaxNameLength);
            e.Property(x => x.Type).HasMaxLength(RunStats.Sync.UpcomingRacesSync.MaxTypeLength);
            e.Property(x => x.City).HasMaxLength(RunStats.Sync.UpcomingRacesSync.MaxCityLength);
            e.Property(x => x.Link).HasMaxLength(RunStats.Sync.UpcomingRacesSync.MaxLinkLength);
        });
        modelBuilder.Entity<SyncRun>(e =>
        {
            e.Property(x => x.Trigger).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.Error).HasMaxLength(2000);
            e.HasIndex(x => x.StartedAtUtc);
        });

        // *Utc columns are timestamptz. The others (*Local) are wall-clock times in the run's own time zone: timestamp
        // without time zone, so PostgreSQL never shifts them. Npgsql only writes the first from Kind = Utc and the second
        // from any other Kind, and a value can carry the wrong Kind, so the converters set it.
        var utc = new ValueConverter<DateTime, DateTime>(
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc), v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        var local = new ValueConverter<DateTime, DateTime>(
            v => DateTime.SpecifyKind(v, DateTimeKind.Unspecified), v => DateTime.SpecifyKind(v, DateTimeKind.Unspecified));
        var dateTimes = modelBuilder.Model.GetEntityTypes()
            .SelectMany(t => t.GetProperties())
            .Where(p => p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?));
        foreach (var property in dateTimes)
        {
            if (property.Name.EndsWith("Utc", StringComparison.Ordinal))
                property.SetValueConverter(utc);
            else
            {
                property.SetValueConverter(local);
                property.SetColumnType("timestamp without time zone");
            }
        }
    }
}
