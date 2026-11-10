using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RunStats.Data;
using RunStats.Processing;
using RunStats.Sync;

namespace RunStats.Tests.Sync;

static class TestSyncJob
{
    public static SyncJob Create(
        IDbContextFactory<AppDbContext> db, FakeGarminGateway garmin, FakeNotionRaces notion, TimeProvider time,
        SyncMetrics metrics, ILogger<SyncJob> logger, int pageSize = 100) =>
        new(db, garmin, new RunProcessing(db, garmin),
            new RecordsSync(garmin, new PersonalRecordUpdate(db, time), new RacePredictionUpdate(db),
                new UpcomingRacesSync(notion, db, new Clock(TimeZoneInfo.Utc, time), NullLogger<UpcomingRacesSync>.Instance)),
            metrics, time, logger) { PageSize = pageSize };
}
