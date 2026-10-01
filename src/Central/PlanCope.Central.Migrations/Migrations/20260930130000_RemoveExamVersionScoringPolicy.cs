using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PlanCope.Central.Api.Data;

#nullable disable

namespace PlanCope.Central.Migrations.Migrations;

[DbContext(typeof(PlanCopeDbContext))]
[Migration("20260930130000_RemoveExamVersionScoringPolicy")]
public partial class RemoveExamVersionScoringPolicy : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ScoringPolicy", schema: "exam", table: "versions");
        migrationBuilder.DropTable(name: "grading_policies", schema: "exam");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ScoringPolicy",
            schema: "exam",
            table: "versions",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "grading_policies",
            schema: "exam",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ExamVersionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ScoringPolicy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                AssignedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Note = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_grading_policies", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_grading_policies_ExamVersionId",
            schema: "exam",
            table: "grading_policies",
            column: "ExamVersionId",
            unique: true);
    }
}
