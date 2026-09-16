using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlanCope.Central.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddStatsRollups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "stats");

            migrationBuilder.CreateTable(
                name: "exam_rollups",
                schema: "stats",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Cue = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SchoolYear = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Course = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExamVersionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    ScoreSum = table.Column<double>(type: "double precision", nullable: false),
                    ScoreMaxSum = table.Column<double>(type: "double precision", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exam_rollups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "exam_rollup_blocks",
                schema: "stats",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RollupId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BlockId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CorrectCount = table.Column<int>(type: "integer", nullable: false),
                    PartialCount = table.Column<int>(type: "integer", nullable: false),
                    IncorrectCount = table.Column<int>(type: "integer", nullable: false),
                    BlankCount = table.Column<int>(type: "integer", nullable: false),
                    UngradableCount = table.Column<int>(type: "integer", nullable: false),
                    ScoreSum = table.Column<double>(type: "double precision", nullable: false),
                    ScoreMaxSum = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exam_rollup_blocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_exam_rollup_blocks_exam_rollups_RollupId",
                        column: x => x.RollupId,
                        principalSchema: "stats",
                        principalTable: "exam_rollups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_exam_rollup_blocks_RollupId_BlockId",
                schema: "stats",
                table: "exam_rollup_blocks",
                columns: new[] { "RollupId", "BlockId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_exam_rollups_Cue_SchoolYear_Course_ExamVersionId",
                schema: "stats",
                table: "exam_rollups",
                columns: new[] { "Cue", "SchoolYear", "Course", "ExamVersionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "exam_rollup_blocks",
                schema: "stats");

            migrationBuilder.DropTable(
                name: "exam_rollups",
                schema: "stats");
        }
    }
}
