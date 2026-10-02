using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlanCope.Central.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddStatsShares : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stats_shares",
                schema: "stats",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    GroupBy = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Filters = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    Snapshot = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stats_shares", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_stats_shares_ExpiresAt_RevokedAt",
                schema: "stats",
                table: "stats_shares",
                columns: new[] { "ExpiresAt", "RevokedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_stats_shares_TokenHash",
                schema: "stats",
                table: "stats_shares",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stats_shares",
                schema: "stats");
        }
    }
}
