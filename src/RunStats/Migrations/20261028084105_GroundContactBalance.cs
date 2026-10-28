using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RunStats.Migrations
{
    /// <inheritdoc />
    public partial class GroundContactBalance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "GroundContactBalanceLeftPercent",
                table: "RunSamples",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "GroundContactBalanceLeftPercent",
                table: "Runs",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "GroundContactBalanceLeftPercent",
                table: "RunLaps",
                type: "float",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GroundContactBalanceLeftPercent",
                table: "RunSamples");

            migrationBuilder.DropColumn(
                name: "GroundContactBalanceLeftPercent",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "GroundContactBalanceLeftPercent",
                table: "RunLaps");
        }
    }
}
