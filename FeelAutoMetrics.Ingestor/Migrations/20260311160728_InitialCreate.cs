using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FeelAutoMetrics.Ingestor.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GlobalAccessLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClientHost = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ClientPort = table.Column<string>(type: "text", nullable: true),
                    RequestMethod = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RequestPath = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    RequestProtocol = table.Column<string>(type: "text", nullable: false),
                    RequestScheme = table.Column<string>(type: "text", nullable: false),
                    RequestPort = table.Column<string>(type: "text", nullable: true),
                    RequestHost = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ResponseStatusCode = table.Column<int>(type: "integer", nullable: false),
                    ResponseContentSize = table.Column<long>(type: "bigint", nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    OriginDurationMs = table.Column<long>(type: "bigint", nullable: true),
                    OverheadMs = table.Column<long>(type: "bigint", nullable: true),
                    RouterName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ServiceName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    UserAgentBrut = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    CountryCode = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    CountryName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CityName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Latitude = table.Column<double>(type: "double precision", nullable: true),
                    Longitude = table.Column<double>(type: "double precision", nullable: true),
                    BrowserName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    BrowserVersion = table.Column<string>(type: "text", nullable: true),
                    OsName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    OsVersion = table.Column<string>(type: "text", nullable: true),
                    DeviceFamily = table.Column<string>(type: "text", nullable: true),
                    IsBot = table.Column<bool>(type: "boolean", nullable: false),
                    BotCategory = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlobalAccessLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GlobalAccessLogs_ClientHost",
                table: "GlobalAccessLogs",
                column: "ClientHost");

            migrationBuilder.CreateIndex(
                name: "IX_GlobalAccessLogs_CountryCode",
                table: "GlobalAccessLogs",
                column: "CountryCode");

            migrationBuilder.CreateIndex(
                name: "IX_GlobalAccessLogs_RequestHost",
                table: "GlobalAccessLogs",
                column: "RequestHost");

            migrationBuilder.CreateIndex(
                name: "IX_GlobalAccessLogs_ResponseStatusCode",
                table: "GlobalAccessLogs",
                column: "ResponseStatusCode");

            migrationBuilder.CreateIndex(
                name: "IX_GlobalAccessLogs_Timestamp",
                table: "GlobalAccessLogs",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GlobalAccessLogs");
        }
    }
}
