using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PlanCope.Central.Api.Data;

#nullable disable

namespace PlanCope.Central.Migrations.Migrations;

[DbContext(typeof(PlanCopeDbContext))]
[Migration("20260820170000_AddNominalAttemptSync")]
public partial class AddNominalAttemptSync : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(name: "GePersonId", schema: "sync", table: "received_student_attempts", type: "integer", nullable: true);
        migrationBuilder.AddColumn<string>(name: "RosterStudentId", schema: "sync", table: "received_student_attempts", type: "character varying(128)", maxLength: 128, nullable: true);
        migrationBuilder.AddColumn<string>(name: "RosterSnapshotId", schema: "sync", table: "received_student_attempts", type: "character varying(128)", maxLength: 128, nullable: true);
        migrationBuilder.AddColumn<string>(name: "RosterSectionId", schema: "sync", table: "received_student_attempts", type: "character varying(128)", maxLength: 128, nullable: true);
        migrationBuilder.AddColumn<string>(name: "StudentFirstName", schema: "sync", table: "received_student_attempts", type: "character varying(256)", maxLength: 256, nullable: true);
        migrationBuilder.AddColumn<string>(name: "StudentLastName", schema: "sync", table: "received_student_attempts", type: "character varying(256)", maxLength: 256, nullable: true);
        migrationBuilder.AddColumn<string>(name: "DocumentLast4", schema: "sync", table: "received_student_attempts", type: "character varying(4)", maxLength: 4, nullable: true);
        migrationBuilder.AddColumn<string>(name: "VerificationSource", schema: "sync", table: "received_student_attempts", type: "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>(name: "VerifiedAt", schema: "sync", table: "received_student_attempts", type: "timestamp with time zone", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "GePersonId", schema: "sync", table: "received_student_attempts");
        migrationBuilder.DropColumn(name: "RosterStudentId", schema: "sync", table: "received_student_attempts");
        migrationBuilder.DropColumn(name: "RosterSnapshotId", schema: "sync", table: "received_student_attempts");
        migrationBuilder.DropColumn(name: "RosterSectionId", schema: "sync", table: "received_student_attempts");
        migrationBuilder.DropColumn(name: "StudentFirstName", schema: "sync", table: "received_student_attempts");
        migrationBuilder.DropColumn(name: "StudentLastName", schema: "sync", table: "received_student_attempts");
        migrationBuilder.DropColumn(name: "DocumentLast4", schema: "sync", table: "received_student_attempts");
        migrationBuilder.DropColumn(name: "VerificationSource", schema: "sync", table: "received_student_attempts");
        migrationBuilder.DropColumn(name: "VerifiedAt", schema: "sync", table: "received_student_attempts");
    }
}
