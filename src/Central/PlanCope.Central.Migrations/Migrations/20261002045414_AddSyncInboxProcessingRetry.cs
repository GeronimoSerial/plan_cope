using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlanCope.Central.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddSyncInboxProcessingRetry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextProcessingAt",
                schema: "sync",
                table: "inbox",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProcessingAttemptCount",
                schema: "sync",
                table: "inbox",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_inbox_Status_NextProcessingAt_CreatedAt",
                schema: "sync",
                table: "inbox",
                columns: new[] { "Status", "NextProcessingAt", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_inbox_Status_NextProcessingAt_CreatedAt",
                schema: "sync",
                table: "inbox");

            migrationBuilder.DropColumn(
                name: "NextProcessingAt",
                schema: "sync",
                table: "inbox");

            migrationBuilder.DropColumn(
                name: "ProcessingAttemptCount",
                schema: "sync",
                table: "inbox");
        }
    }
}
