using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FeelAutoMetrics.Ingestor.Migrations
{
    /// <inheritdoc />
    public partial class AddAiAnalysis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiAnalyses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AnalyzedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LogsAnalyzed = table.Column<int>(type: "integer", nullable: false),
                    ActionsTaken = table.Column<int>(type: "integer", nullable: false),
                    Summary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    LogsFromTimestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LogsToTimestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiAnalyses", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiAnalyses_AnalyzedAt",
                table: "AiAnalyses",
                column: "AnalyzedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiAnalyses");
        }
    }
}
