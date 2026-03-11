using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FeelAutoMetrics.Ingestor.Migrations
{
    /// <inheritdoc />
    public partial class AddBannedIps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BannedIps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    BannedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BannedIps", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BannedIps_IpAddress",
                table: "BannedIps",
                column: "IpAddress",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BannedIps");
        }
    }
}
