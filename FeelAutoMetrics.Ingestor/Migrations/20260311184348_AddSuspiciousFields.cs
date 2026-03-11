using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FeelAutoMetrics.Ingestor.Migrations
{
    /// <inheritdoc />
    public partial class AddSuspiciousFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSuspicious",
                table: "GlobalAccessLogs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ThreatType",
                table: "GlobalAccessLogs",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsSuspicious",
                table: "GlobalAccessLogs");

            migrationBuilder.DropColumn(
                name: "ThreatType",
                table: "GlobalAccessLogs");
        }
    }
}
