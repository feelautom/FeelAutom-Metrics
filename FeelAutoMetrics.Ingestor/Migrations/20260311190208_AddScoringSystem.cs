using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FeelAutoMetrics.Ingestor.Migrations
{
    /// <inheritdoc />
    public partial class AddScoringSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BanCount",
                table: "BannedIps",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "BannedIps",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "IpThreatScores",
                columns: table => new
                {
                    IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    LastHit = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FirstSeen = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IpThreatScores", x => x.IpAddress);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IpThreatScores");

            migrationBuilder.DropColumn(
                name: "BanCount",
                table: "BannedIps");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "BannedIps");
        }
    }
}
