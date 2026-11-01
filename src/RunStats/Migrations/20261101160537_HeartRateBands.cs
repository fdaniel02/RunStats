using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RunStats.Migrations
{
    /// <inheritdoc />
    public partial class HeartRateBands : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RunHeartRateBands",
                columns: table => new
                {
                    RunId = table.Column<long>(type: "bigint", nullable: false),
                    HeartRate = table.Column<short>(type: "smallint", nullable: false),
                    Seconds = table.Column<int>(type: "int", nullable: false),
                    DistanceMeters = table.Column<double>(type: "float", nullable: false)
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RunHeartRateBands");
        }
    }
}
