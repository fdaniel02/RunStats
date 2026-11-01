using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RunStats.Migrations
{
    /// <inheritdoc />
    public partial class BestEfforts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RunBestEfforts",
                columns: table => new
                {
                    RunId = table.Column<long>(type: "bigint", nullable: false),
                    TypeId = table.Column<int>(type: "int", nullable: false),
                    Seconds = table.Column<double>(type: "float", nullable: false)
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RunBestEfforts");
        }
    }
}
