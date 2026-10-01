using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlanCope.Central.Migrations.Migrations;

public partial class AddOffRosterAttemptIdentity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "DocumentHmac", schema: "sync", table: "received_student_attempts",
            type: "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<bool>(
            name: "OffRoster", schema: "sync", table: "received_student_attempts",
            type: "boolean", nullable: false, defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "DocumentHmac", schema: "sync", table: "received_student_attempts");
        migrationBuilder.DropColumn(name: "OffRoster", schema: "sync", table: "received_student_attempts");
    }
}
