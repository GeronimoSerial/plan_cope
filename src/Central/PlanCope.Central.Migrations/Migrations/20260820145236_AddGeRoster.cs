using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlanCope.Central.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddGeRoster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "roster");

            migrationBuilder.CreateTable(
                name: "roster_snapshots",
                schema: "roster",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Cue = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SchoolYear = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    FetchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Checksum = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SectionCount = table.Column<int>(type: "integer", nullable: false),
                    StudentCount = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roster_snapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "roster_sections",
                schema: "roster",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SnapshotId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    GeSectionId = table.Column<int>(type: "integer", nullable: true),
                    Course = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Division = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Level = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Shift = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roster_sections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_roster_sections_roster_snapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalSchema: "roster",
                        principalTable: "roster_snapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "roster_students",
                schema: "roster",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SnapshotId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SectionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    GePersonId = table.Column<int>(type: "integer", nullable: false),
                    Document = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FirstName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    LastName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roster_students", x => x.Id);
                    table.ForeignKey(
                        name: "FK_roster_students_roster_sections_SectionId",
                        column: x => x.SectionId,
                        principalSchema: "roster",
                        principalTable: "roster_sections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_roster_students_roster_snapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalSchema: "roster",
                        principalTable: "roster_snapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_roster_sections_SnapshotId",
                schema: "roster",
                table: "roster_sections",
                column: "SnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_roster_sections_SnapshotId_GeSectionId_Course_Division_Leve~",
                schema: "roster",
                table: "roster_sections",
                columns: new[] { "SnapshotId", "GeSectionId", "Course", "Division", "Level", "Shift" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_roster_snapshots_Cue_SchoolYear_Checksum",
                schema: "roster",
                table: "roster_snapshots",
                columns: new[] { "Cue", "SchoolYear", "Checksum" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_roster_snapshots_Cue_SchoolYear_FetchedAt",
                schema: "roster",
                table: "roster_snapshots",
                columns: new[] { "Cue", "SchoolYear", "FetchedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_roster_students_SectionId",
                schema: "roster",
                table: "roster_students",
                column: "SectionId");

            migrationBuilder.CreateIndex(
                name: "IX_roster_students_SnapshotId_Document",
                schema: "roster",
                table: "roster_students",
                columns: new[] { "SnapshotId", "Document" });

            migrationBuilder.CreateIndex(
                name: "IX_roster_students_SnapshotId_SectionId_GePersonId",
                schema: "roster",
                table: "roster_students",
                columns: new[] { "SnapshotId", "SectionId", "GePersonId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "roster_students",
                schema: "roster");

            migrationBuilder.DropTable(
                name: "roster_sections",
                schema: "roster");

            migrationBuilder.DropTable(
                name: "roster_snapshots",
                schema: "roster");
        }
    }
}
