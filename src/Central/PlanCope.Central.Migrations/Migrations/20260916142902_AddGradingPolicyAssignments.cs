using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlanCope.Central.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddGradingPolicyAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                constraints: table =>
                {
                    table.PrimaryKey("PK_grading_policies", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_grading_policies_ExamVersionId",
                schema: "exam",
                table: "grading_policies",
                column: "ExamVersionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "grading_policies",
                schema: "exam");
        }
    }
}
