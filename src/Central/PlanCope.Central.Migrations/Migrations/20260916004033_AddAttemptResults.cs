using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlanCope.Central.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddAttemptResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "attempt_results",
                schema: "sync",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReceivedStudentAttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    GradingSchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    ScoringPolicy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Score = table.Column<decimal>(type: "numeric", nullable: true),
                    ScoreMax = table.Column<decimal>(type: "numeric", nullable: true),
                    BlocksJson = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    GradedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attempt_results", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_attempt_results_ReceivedStudentAttemptId_GradingSchemaVersi~",
                schema: "sync",
                table: "attempt_results",
                columns: new[] { "ReceivedStudentAttemptId", "GradingSchemaVersion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attempt_results",
                schema: "sync");
        }
    }
}
