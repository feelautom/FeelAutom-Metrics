using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FeelAutoMetrics.Ingestor.Migrations
{
    /// <inheritdoc />
    public partial class AddExcludedIps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExcludedIps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: false),
                    Label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExcludedIps", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExcludedIps_IpAddress",
                table: "ExcludedIps",
                column: "IpAddress",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExcludedIps");
        }
    }
}
