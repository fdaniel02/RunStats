using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RunStats.Migrations
{
    /// <inheritdoc />
    public partial class RunProcessing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RunsDownloaded",
                table: "SyncRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RunsFailedToProcess",
                table: "SyncRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RunsToProcess",
                table: "SyncRuns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProcessingError",
                table: "Runs",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            // RunsProcessed used to count downloads; it now counts the processing that follows them.
            migrationBuilder.Sql("UPDATE SyncRuns SET RunsDownloaded = RunsProcessed, RunsProcessed = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE SyncRuns SET RunsProcessed = RunsDownloaded;");

            migrationBuilder.DropColumn(
                name: "RunsDownloaded",
                table: "SyncRuns");

            migrationBuilder.DropColumn(
                name: "RunsFailedToProcess",
                table: "SyncRuns");

            migrationBuilder.DropColumn(
                name: "RunsToProcess",
                table: "SyncRuns");

            migrationBuilder.DropColumn(
                name: "ProcessingError",
                table: "Runs");
        }
    }
}
