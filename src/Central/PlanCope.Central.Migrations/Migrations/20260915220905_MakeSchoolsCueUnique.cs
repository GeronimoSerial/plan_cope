using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlanCope.Central.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class MakeSchoolsCueUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    dup_count integer;
                    dup_list text;
                BEGIN
                    SELECT count(*), string_agg(DISTINCT "Cue"::text, ', ')
                    INTO dup_count, dup_list
                    FROM (
                        SELECT "Cue"
                        FROM core.schools
                        GROUP BY "Cue"
                        HAVING count(*) > 1
                    ) dupes;

                    IF dup_count > 0 THEN
                        RAISE EXCEPTION 'core.schools has % duplicate Cue value(s) that must be resolved before the unique index can be created: %', dup_count, dup_list;
                    END IF;
                END $$;
                """);

            migrationBuilder.DropIndex(
                name: "IX_schools_Cue",
                schema: "core",
                table: "schools");

            migrationBuilder.CreateIndex(
                name: "IX_schools_Cue",
                schema: "core",
                table: "schools",
                column: "Cue",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_schools_Cue",
                schema: "core",
                table: "schools");

            migrationBuilder.CreateIndex(
                name: "IX_schools_Cue",
                schema: "core",
                table: "schools",
                column: "Cue");
        }
    }
}
