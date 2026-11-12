using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RunStats.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DataProtectionKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FriendlyName = table.Column<string>(type: "text", nullable: true),
                    Xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PersonalRecords",
                columns: table => new
                {
                    TypeId = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<double>(type: "double precision", nullable: false),
                    RunId = table.Column<long>(type: "bigint", nullable: true),
                    AchievedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AchievedAtLocal = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    RawJson = table.Column<string>(type: "text", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonalRecords", x => x.TypeId);
                });

            migrationBuilder.CreateTable(
                name: "RacePredictions",
                columns: table => new
                {
                    TypeId = table.Column<int>(type: "integer", nullable: false),
                    Seconds = table.Column<double>(type: "double precision", nullable: false),
                    PredictedOn = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RacePredictions", x => x.TypeId);
                });

            migrationBuilder.CreateTable(
                name: "Runs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TypeKey = table.Column<string>(type: "character varying(50)", unicode: false, maxLength: 50, nullable: false),
                    TypeId = table.Column<int>(type: "integer", nullable: false),
                    ParentTypeId = table.Column<int>(type: "integer", nullable: false),
                    IsRace = table.Column<bool>(type: "boolean", nullable: false),
                    StartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartLocal = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    MinHeartRate = table.Column<short>(type: "smallint", nullable: true),
                    MinElevationMeters = table.Column<double>(type: "double precision", nullable: true),
                    MaxElevationMeters = table.Column<double>(type: "double precision", nullable: true),
                    AerobicTrainingEffect = table.Column<double>(type: "double precision", nullable: true),
                    AnaerobicTrainingEffect = table.Column<double>(type: "double precision", nullable: true),
                    TrainingEffectLabel = table.Column<string>(type: "character varying(50)", unicode: false, maxLength: 50, nullable: true),
                    TrainingLoad = table.Column<double>(type: "double precision", nullable: true),
                    Vo2Max = table.Column<double>(type: "double precision", nullable: true),
                    Steps = table.Column<int>(type: "integer", nullable: true),
                    AverageTemperatureCelsius = table.Column<double>(type: "double precision", nullable: true),
                    PaceVariationPercent = table.Column<double>(type: "double precision", nullable: true),
                    WeatherTemperatureCelsius = table.Column<double>(type: "double precision", nullable: true),
                    WeatherFeelsLikeCelsius = table.Column<double>(type: "double precision", nullable: true),
                    WeatherDewPointCelsius = table.Column<double>(type: "double precision", nullable: true),
                    WeatherHumidityPercent = table.Column<int>(type: "integer", nullable: true),
                    WeatherWindSpeedKmh = table.Column<double>(type: "double precision", nullable: true),
                    WeatherWindDirection = table.Column<string>(type: "character varying(3)", unicode: false, maxLength: 3, nullable: true),
                    WeatherStation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    StartLatitude = table.Column<double>(type: "double precision", nullable: true),
                    StartLongitude = table.Column<double>(type: "double precision", nullable: true),
                    EndLatitude = table.Column<double>(type: "double precision", nullable: true),
                    EndLongitude = table.Column<double>(type: "double precision", nullable: true),
                    ProcessorVersion = table.Column<int>(type: "integer", nullable: false),
                    ProcessingError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DistanceMeters = table.Column<double>(type: "double precision", nullable: false),
                    DurationSeconds = table.Column<double>(type: "double precision", nullable: false),
                    MovingDurationSeconds = table.Column<double>(type: "double precision", nullable: true),
                    ElapsedDurationSeconds = table.Column<double>(type: "double precision", nullable: true),
                    AverageSpeedMps = table.Column<double>(type: "double precision", nullable: true),
                    AverageMovingSpeedMps = table.Column<double>(type: "double precision", nullable: true),
                    GradeAdjustedSpeedMps = table.Column<double>(type: "double precision", nullable: true),
                    MaxSpeedMps = table.Column<double>(type: "double precision", nullable: true),
                    AverageHeartRate = table.Column<short>(type: "smallint", nullable: true),
                    MaxHeartRate = table.Column<short>(type: "smallint", nullable: true),
                    AverageCadenceSpm = table.Column<double>(type: "double precision", nullable: true),
                    MaxCadenceSpm = table.Column<double>(type: "double precision", nullable: true),
                    StrideLengthCm = table.Column<double>(type: "double precision", nullable: true),
                    GroundContactTimeMs = table.Column<double>(type: "double precision", nullable: true),
                    VerticalOscillationCm = table.Column<double>(type: "double precision", nullable: true),
                    VerticalRatioPercent = table.Column<double>(type: "double precision", nullable: true),
                    GroundContactBalanceLeftPercent = table.Column<double>(type: "double precision", nullable: true),
                    AveragePowerWatts = table.Column<double>(type: "double precision", nullable: true),
                    MaxPowerWatts = table.Column<double>(type: "double precision", nullable: true),
                    NormalizedPowerWatts = table.Column<double>(type: "double precision", nullable: true),
                    ElevationGainMeters = table.Column<double>(type: "double precision", nullable: true),
                    ElevationLossMeters = table.Column<double>(type: "double precision", nullable: true),
                    Calories = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Runs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SyncRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Trigger = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RunsTotal = table.Column<int>(type: "integer", nullable: true),
                    RunsDownloaded = table.Column<int>(type: "integer", nullable: false),
                    RunsToProcess = table.Column<int>(type: "integer", nullable: true),
                    RunsProcessed = table.Column<int>(type: "integer", nullable: false),
                    RunsFailedToProcess = table.Column<int>(type: "integer", nullable: false),
                    RunsAdded = table.Column<int>(type: "integer", nullable: false),
                    RunsUpdated = table.Column<int>(type: "integer", nullable: false),
                    RunsDeleted = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UpcomingRaces",
                columns: table => new
                {
                    NotionPageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    DistanceMeters = table.Column<double>(type: "double precision", nullable: true),
                    Type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    IsRegistered = table.Column<bool>(type: "boolean", nullable: false),
                    City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Link = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    TargetSeconds = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UpcomingRaces", x => x.NotionPageId);
                });

            migrationBuilder.CreateTable(
                name: "YearGoals",
                columns: table => new
                {
                    Year = table.Column<int>(type: "integer", nullable: false),
                    DistanceMeters = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YearGoals", x => x.Year);
                });

            migrationBuilder.CreateTable(
                name: "RunBestEfforts",
                columns: table => new
                {
                    RunId = table.Column<long>(type: "bigint", nullable: false),
                    TypeId = table.Column<int>(type: "integer", nullable: false),
                    Seconds = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunBestEfforts", x => new { x.RunId, x.TypeId });
                    table.ForeignKey(
                        name: "FK_RunBestEfforts_Runs_RunId",
                        column: x => x.RunId,
                        principalTable: "Runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RunHeartRateBands",
                columns: table => new
                {
                    RunId = table.Column<long>(type: "bigint", nullable: false),
                    HeartRate = table.Column<short>(type: "smallint", nullable: false),
                    Seconds = table.Column<int>(type: "integer", nullable: false),
                    DistanceMeters = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunHeartRateBands", x => new { x.RunId, x.HeartRate });
                    table.ForeignKey(
                        name: "FK_RunHeartRateBands_Runs_RunId",
                        column: x => x.RunId,
                        principalTable: "Runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RunLaps",
                columns: table => new
                {
                    RunId = table.Column<long>(type: "bigint", nullable: false),
                    LapIndex = table.Column<int>(type: "integer", nullable: false),
                    StartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IntensityType = table.Column<string>(type: "character varying(50)", unicode: false, maxLength: 50, nullable: true),
                    PaceVariationPercent = table.Column<double>(type: "double precision", nullable: true),
                    DistanceMeters = table.Column<double>(type: "double precision", nullable: false),
                    DurationSeconds = table.Column<double>(type: "double precision", nullable: false),
                    MovingDurationSeconds = table.Column<double>(type: "double precision", nullable: true),
                    ElapsedDurationSeconds = table.Column<double>(type: "double precision", nullable: true),
                    AverageSpeedMps = table.Column<double>(type: "double precision", nullable: true),
                    AverageMovingSpeedMps = table.Column<double>(type: "double precision", nullable: true),
                    GradeAdjustedSpeedMps = table.Column<double>(type: "double precision", nullable: true),
                    MaxSpeedMps = table.Column<double>(type: "double precision", nullable: true),
                    AverageHeartRate = table.Column<short>(type: "smallint", nullable: true),
                    MaxHeartRate = table.Column<short>(type: "smallint", nullable: true),
                    AverageCadenceSpm = table.Column<double>(type: "double precision", nullable: true),
                    MaxCadenceSpm = table.Column<double>(type: "double precision", nullable: true),
                    StrideLengthCm = table.Column<double>(type: "double precision", nullable: true),
                    GroundContactTimeMs = table.Column<double>(type: "double precision", nullable: true),
                    VerticalOscillationCm = table.Column<double>(type: "double precision", nullable: true),
                    VerticalRatioPercent = table.Column<double>(type: "double precision", nullable: true),
                    GroundContactBalanceLeftPercent = table.Column<double>(type: "double precision", nullable: true),
                    AveragePowerWatts = table.Column<double>(type: "double precision", nullable: true),
                    MaxPowerWatts = table.Column<double>(type: "double precision", nullable: true),
                    NormalizedPowerWatts = table.Column<double>(type: "double precision", nullable: true),
                    ElevationGainMeters = table.Column<double>(type: "double precision", nullable: true),
                    ElevationLossMeters = table.Column<double>(type: "double precision", nullable: true),
                    Calories = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunLaps", x => new { x.RunId, x.LapIndex });
                    table.ForeignKey(
                        name: "FK_RunLaps_Runs_RunId",
                        column: x => x.RunId,
                        principalTable: "Runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RunPaceBands",
                columns: table => new
                {
                    RunId = table.Column<long>(type: "bigint", nullable: false),
                    PaceSecondsPerKm = table.Column<short>(type: "smallint", nullable: false),
                    Seconds = table.Column<int>(type: "integer", nullable: false),
                    FlatSeconds = table.Column<int>(type: "integer", nullable: false),
                    CadenceSpm = table.Column<float>(type: "real", nullable: true),
                    StepLengthMm = table.Column<float>(type: "real", nullable: true),
                    GroundContactTimeMs = table.Column<float>(type: "real", nullable: true),
                    VerticalOscillationMm = table.Column<float>(type: "real", nullable: true),
                    VerticalRatioPercent = table.Column<float>(type: "real", nullable: true),
                    HeartRate = table.Column<float>(type: "real", nullable: true),
                    PowerWatts = table.Column<float>(type: "real", nullable: true),
                    GroundContactBalanceLeftPercent = table.Column<float>(type: "real", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunPaceBands", x => new { x.RunId, x.PaceSecondsPerKm });
                    table.ForeignKey(
                        name: "FK_RunPaceBands_Runs_RunId",
                        column: x => x.RunId,
                        principalTable: "Runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RunRawData",
                columns: table => new
                {
                    RunId = table.Column<long>(type: "bigint", nullable: false),
                    ListJson = table.Column<string>(type: "text", nullable: false),
                    DetailJson = table.Column<string>(type: "text", nullable: false),
                    SplitsJson = table.Column<string>(type: "text", nullable: true),
                    HeartRateZonesJson = table.Column<string>(type: "text", nullable: true),
                    WeatherJson = table.Column<string>(type: "text", nullable: true),
                    FitFile = table.Column<byte[]>(type: "bytea", nullable: true),
                    DownloadedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunRawData", x => x.RunId);
                    table.ForeignKey(
                        name: "FK_RunRawData_Runs_RunId",
                        column: x => x.RunId,
                        principalTable: "Runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RunSamples",
                columns: table => new
                {
                    RunId = table.Column<long>(type: "bigint", nullable: false),
                    OffsetSeconds = table.Column<int>(type: "integer", nullable: false),
                    LatitudeSemicircles = table.Column<int>(type: "integer", nullable: true),
                    LongitudeSemicircles = table.Column<int>(type: "integer", nullable: true),
                    DistanceMeters = table.Column<float>(type: "real", nullable: true),
                    SpeedMps = table.Column<float>(type: "real", nullable: true),
                    AltitudeMeters = table.Column<float>(type: "real", nullable: true),
                    HeartRate = table.Column<byte>(type: "smallint", nullable: true),
                    CadenceSpm = table.Column<float>(type: "real", nullable: true),
                    StepLengthMm = table.Column<short>(type: "smallint", nullable: true),
                    GroundContactTimeMs = table.Column<float>(type: "real", nullable: true),
                    VerticalOscillationMm = table.Column<float>(type: "real", nullable: true),
                    VerticalRatioPercent = table.Column<float>(type: "real", nullable: true),
                    GroundContactBalanceLeftPercent = table.Column<float>(type: "real", nullable: true),
                    PowerWatts = table.Column<short>(type: "smallint", nullable: true),
                    TemperatureCelsius = table.Column<short>(type: "smallint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunSamples", x => new { x.RunId, x.OffsetSeconds });
                    table.ForeignKey(
                        name: "FK_RunSamples_Runs_RunId",
                        column: x => x.RunId,
                        principalTable: "Runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RunTracks",
                columns: table => new
                {
                    RunId = table.Column<long>(type: "bigint", nullable: false),
                    EncodedPolyline = table.Column<string>(type: "text", unicode: false, nullable: false),
                    PointCount = table.Column<int>(type: "integer", nullable: false),
                    MinLatitude = table.Column<double>(type: "double precision", nullable: false),
                    MaxLatitude = table.Column<double>(type: "double precision", nullable: false),
                    MinLongitude = table.Column<double>(type: "double precision", nullable: false),
                    MaxLongitude = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunTracks", x => x.RunId);
                    table.ForeignKey(
                        name: "FK_RunTracks_Runs_RunId",
                        column: x => x.RunId,
                        principalTable: "Runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Runs_StartUtc",
                table: "Runs",
                column: "StartUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SyncRuns_StartedAtUtc",
                table: "SyncRuns",
                column: "StartedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DataProtectionKeys");

            migrationBuilder.DropTable(
                name: "PersonalRecords");

            migrationBuilder.DropTable(
                name: "RacePredictions");

            migrationBuilder.DropTable(
                name: "RunBestEfforts");

            migrationBuilder.DropTable(
                name: "RunHeartRateBands");

            migrationBuilder.DropTable(
                name: "RunLaps");

            migrationBuilder.DropTable(
                name: "RunPaceBands");

            migrationBuilder.DropTable(
                name: "RunRawData");

            migrationBuilder.DropTable(
                name: "RunSamples");

            migrationBuilder.DropTable(
                name: "RunTracks");

            migrationBuilder.DropTable(
                name: "SyncRuns");

            migrationBuilder.DropTable(
                name: "UpcomingRaces");

            migrationBuilder.DropTable(
                name: "YearGoals");

            migrationBuilder.DropTable(
                name: "Runs");
        }
    }
}
