using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RunStats.Migrations
{
    /// <inheritdoc />
    public partial class PaceBands : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RunPaceBands",
                columns: table => new
                {
                    RunId = table.Column<long>(type: "bigint", nullable: false),
                    PaceSecondsPerKm = table.Column<short>(type: "smallint", nullable: false),
                    Seconds = table.Column<int>(type: "int", nullable: false),
                    FlatSeconds = table.Column<int>(type: "int", nullable: false),
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RunPaceBands");
        }
    }
}
