using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlanCope.Central.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class TrackAttemptAttribution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AttributionReason",
                schema: "sync",
                table: "received_student_attempts",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttributionStatus",
                schema: "sync",
                table: "received_student_attempts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "pending");

            migrationBuilder.AddColumn<string>(
                name: "Course",
                schema: "sync",
                table: "delivery_sessions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SchoolYear",
                schema: "sync",
                table: "delivery_sessions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                schema: "sync",
                table: "attempt_results",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AttributionReason",
                schema: "sync",
                table: "received_student_attempts");

            migrationBuilder.DropColumn(
                name: "AttributionStatus",
                schema: "sync",
                table: "received_student_attempts");

            migrationBuilder.DropColumn(
                name: "Course",
                schema: "sync",
                table: "delivery_sessions");

            migrationBuilder.DropColumn(
                name: "SchoolYear",
                schema: "sync",
                table: "delivery_sessions");

            migrationBuilder.DropColumn(
                name: "Reason",
                schema: "sync",
                table: "attempt_results");
        }
    }
}
