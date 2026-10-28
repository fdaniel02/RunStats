using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RunStats.Migrations
{
    /// <inheritdoc />
    public partial class PaceVariation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "PaceVariationPercent",
                table: "Runs",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PaceVariationPercent",
                table: "RunLaps",
                type: "float",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaceVariationPercent",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "PaceVariationPercent",
                table: "RunLaps");
        }
    }
}
