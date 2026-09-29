using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlanCope.Central.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddExamVersionSourceVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceVersionId",
                schema: "exam",
                table: "versions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SourceVersionId",
                schema: "exam",
                table: "versions");
        }
    }
}
