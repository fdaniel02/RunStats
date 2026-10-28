using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RunStats.Migrations
{
    /// <inheritdoc />
    public partial class Weather : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "WeatherDewPointCelsius",
                table: "Runs",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "WeatherFeelsLikeCelsius",
                table: "Runs",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WeatherHumidityPercent",
                table: "Runs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WeatherStation",
                table: "Runs",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "WeatherTemperatureCelsius",
                table: "Runs",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WeatherWindDirection",
                table: "Runs",
                type: "varchar(3)",
                unicode: false,
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "WeatherWindSpeedKmh",
                table: "Runs",
                type: "float",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WeatherDewPointCelsius",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "WeatherFeelsLikeCelsius",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "WeatherHumidityPercent",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "WeatherStation",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "WeatherTemperatureCelsius",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "WeatherWindDirection",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "WeatherWindSpeedKmh",
                table: "Runs");
        }
    }
}
