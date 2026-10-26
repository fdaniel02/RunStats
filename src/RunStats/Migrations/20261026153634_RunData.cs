using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RunStats.Migrations
{
    /// <inheritdoc />
    public partial class RunData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PersonalRecords",
                columns: table => new
                {
                    TypeId = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<double>(type: "float", nullable: false),
                    RunId = table.Column<long>(type: "bigint", nullable: true),
                    AchievedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AchievedAtLocal = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RawJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonalRecords", x => x.TypeId);
                });

            migrationBuilder.CreateTable(
                name: "Runs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TypeKey = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    TypeId = table.Column<int>(type: "int", nullable: false),
                    ParentTypeId = table.Column<int>(type: "int", nullable: false),
                    IsRace = table.Column<bool>(type: "bit", nullable: false),
                    StartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartLocal = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MinHeartRate = table.Column<short>(type: "smallint", nullable: true),
                    MinElevationMeters = table.Column<double>(type: "float", nullable: true),
                    MaxElevationMeters = table.Column<double>(type: "float", nullable: true),
                    AerobicTrainingEffect = table.Column<double>(type: "float", nullable: true),
                    AnaerobicTrainingEffect = table.Column<double>(type: "float", nullable: true),
                    TrainingLoad = table.Column<double>(type: "float", nullable: true),
                    Vo2Max = table.Column<double>(type: "float", nullable: true),
                    Steps = table.Column<int>(type: "int", nullable: true),
                    AverageTemperatureCelsius = table.Column<double>(type: "float", nullable: true),
                    StartLatitude = table.Column<double>(type: "float", nullable: true),
                    StartLongitude = table.Column<double>(type: "float", nullable: true),
                    EndLatitude = table.Column<double>(type: "float", nullable: true),
                    EndLongitude = table.Column<double>(type: "float", nullable: true),
                    ProcessorVersion = table.Column<int>(type: "int", nullable: false),
                    DistanceMeters = table.Column<double>(type: "float", nullable: false),
                    DurationSeconds = table.Column<double>(type: "float", nullable: false),
                    MovingDurationSeconds = table.Column<double>(type: "float", nullable: true),
                    ElapsedDurationSeconds = table.Column<double>(type: "float", nullable: true),
                    AverageSpeedMps = table.Column<double>(type: "float", nullable: true),
                    AverageMovingSpeedMps = table.Column<double>(type: "float", nullable: true),
                    GradeAdjustedSpeedMps = table.Column<double>(type: "float", nullable: true),
                    MaxSpeedMps = table.Column<double>(type: "float", nullable: true),
                    AverageHeartRate = table.Column<short>(type: "smallint", nullable: true),
                    MaxHeartRate = table.Column<short>(type: "smallint", nullable: true),
                    AverageCadenceSpm = table.Column<double>(type: "float", nullable: true),
                    MaxCadenceSpm = table.Column<double>(type: "float", nullable: true),
                    StrideLengthCm = table.Column<double>(type: "float", nullable: true),
                    GroundContactTimeMs = table.Column<double>(type: "float", nullable: true),
                    VerticalOscillationCm = table.Column<double>(type: "float", nullable: true),
                    VerticalRatioPercent = table.Column<double>(type: "float", nullable: true),
                    AveragePowerWatts = table.Column<double>(type: "float", nullable: true),
                    MaxPowerWatts = table.Column<double>(type: "float", nullable: true),
                    NormalizedPowerWatts = table.Column<double>(type: "float", nullable: true),
                    ElevationGainMeters = table.Column<double>(type: "float", nullable: true),
                    ElevationLossMeters = table.Column<double>(type: "float", nullable: true),
                    Calories = table.Column<double>(type: "float", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Runs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SyncRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Trigger = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FinishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RunsTotal = table.Column<int>(type: "int", nullable: true),
                    RunsProcessed = table.Column<int>(type: "int", nullable: false),
                    RunsAdded = table.Column<int>(type: "int", nullable: false),
                    RunsUpdated = table.Column<int>(type: "int", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RunLaps",
                columns: table => new
                {
                    RunId = table.Column<long>(type: "bigint", nullable: false),
                    LapIndex = table.Column<int>(type: "int", nullable: false),
                    StartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IntensityType = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    DistanceMeters = table.Column<double>(type: "float", nullable: false),
                    DurationSeconds = table.Column<double>(type: "float", nullable: false),
                    MovingDurationSeconds = table.Column<double>(type: "float", nullable: true),
                    ElapsedDurationSeconds = table.Column<double>(type: "float", nullable: true),
                    AverageSpeedMps = table.Column<double>(type: "float", nullable: true),
                    AverageMovingSpeedMps = table.Column<double>(type: "float", nullable: true),
                    GradeAdjustedSpeedMps = table.Column<double>(type: "float", nullable: true),
                    MaxSpeedMps = table.Column<double>(type: "float", nullable: true),
                    AverageHeartRate = table.Column<short>(type: "smallint", nullable: true),
                    MaxHeartRate = table.Column<short>(type: "smallint", nullable: true),
                    AverageCadenceSpm = table.Column<double>(type: "float", nullable: true),
                    MaxCadenceSpm = table.Column<double>(type: "float", nullable: true),
                    StrideLengthCm = table.Column<double>(type: "float", nullable: true),
                    GroundContactTimeMs = table.Column<double>(type: "float", nullable: true),
                    VerticalOscillationCm = table.Column<double>(type: "float", nullable: true),
                    VerticalRatioPercent = table.Column<double>(type: "float", nullable: true),
                    AveragePowerWatts = table.Column<double>(type: "float", nullable: true),
                    MaxPowerWatts = table.Column<double>(type: "float", nullable: true),
                    NormalizedPowerWatts = table.Column<double>(type: "float", nullable: true),
                    ElevationGainMeters = table.Column<double>(type: "float", nullable: true),
                    ElevationLossMeters = table.Column<double>(type: "float", nullable: true),
                    Calories = table.Column<double>(type: "float", nullable: true)
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
                name: "RunRawData",
                columns: table => new
                {
                    RunId = table.Column<long>(type: "bigint", nullable: false),
                    ListJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DetailJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SplitsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HeartRateZonesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WeatherJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FitFile = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    DownloadedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
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
                    OffsetSeconds = table.Column<int>(type: "int", nullable: false),
                    LatitudeSemicircles = table.Column<int>(type: "int", nullable: true),
                    LongitudeSemicircles = table.Column<int>(type: "int", nullable: true),
                    DistanceMeters = table.Column<float>(type: "real", nullable: true),
                    SpeedMps = table.Column<float>(type: "real", nullable: true),
                    AltitudeMeters = table.Column<float>(type: "real", nullable: true),
                    HeartRate = table.Column<byte>(type: "tinyint", nullable: true),
                    CadenceSpm = table.Column<float>(type: "real", nullable: true),
                    StepLengthMm = table.Column<short>(type: "smallint", nullable: true),
                    GroundContactTimeMs = table.Column<float>(type: "real", nullable: true),
                    VerticalOscillationMm = table.Column<float>(type: "real", nullable: true),
                    VerticalRatioPercent = table.Column<float>(type: "real", nullable: true),
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
                    EncodedPolyline = table.Column<string>(type: "varchar(max)", unicode: false, nullable: false),
                    PointCount = table.Column<int>(type: "int", nullable: false),
                    MinLatitude = table.Column<double>(type: "float", nullable: false),
                    MaxLatitude = table.Column<double>(type: "float", nullable: false),
                    MinLongitude = table.Column<double>(type: "float", nullable: false),
                    MaxLongitude = table.Column<double>(type: "float", nullable: false)
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

            // EF Core can't declare table compression. Samples are most of the database; with per-second data, page
            // compression made them about a fifth smaller (52 instead of 66 bytes per row).
            migrationBuilder.Sql("ALTER TABLE [RunSamples] REBUILD WITH (DATA_COMPRESSION = PAGE);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PersonalRecords");

            migrationBuilder.DropTable(
                name: "RunLaps");

            migrationBuilder.DropTable(
                name: "RunRawData");

            migrationBuilder.DropTable(
                name: "RunSamples");

            migrationBuilder.DropTable(
                name: "RunTracks");

            migrationBuilder.DropTable(
                name: "SyncRuns");

            migrationBuilder.DropTable(
                name: "Runs");
        }
    }
}
