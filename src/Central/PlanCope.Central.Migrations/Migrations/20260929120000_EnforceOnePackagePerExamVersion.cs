using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlanCope.Central.Migrations.Migrations;

/// <inheritdoc />
public partial class EnforceOnePackagePerExamVersion : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "INSERT INTO core.roles (\"Id\", \"Code\", \"Name\", \"Description\", \"CreatedAt\")" +
            " VALUES ('role-exam-author', 'ExamAuthor', 'Exam author', 'Can create and publish exams.', CURRENT_TIMESTAMP)" +
            " ON CONFLICT (\"Code\") DO NOTHING;");

        // Delivery markers are keyed by package id; divergent checksums mean nodes may hold
        // different content, so fail instead of guessing which package is safe to retain.
        migrationBuilder.Sql(
            "DO $$ DECLARE conflict record; BEGIN " +
            " SELECT keeper.\"ExamVersionId\" AS version_id, keeper.\"Id\" AS keeper_id, removed.\"Id\" AS removed_id " +
            " INTO conflict FROM (" +
            "  SELECT *, FIRST_VALUE(\"Id\") OVER (PARTITION BY \"ExamVersionId\" ORDER BY COALESCE(\"PublishedAt\", \"CreatedAt\"), \"Id\") AS keeper_id, " +
            "         ROW_NUMBER() OVER (PARTITION BY \"ExamVersionId\" ORDER BY COALESCE(\"PublishedAt\", \"CreatedAt\"), \"Id\") AS row_number " +
            "  FROM publication.packages) ranked " +
            " JOIN publication.packages keeper ON keeper.\"Id\" = ranked.keeper_id " +
            " JOIN publication.packages removed ON removed.\"Id\" = ranked.\"Id\" " +
            " WHERE ranked.row_number > 1 AND removed.\"Checksum\" IS DISTINCT FROM keeper.\"Checksum\" LIMIT 1; " +
            " IF FOUND THEN RAISE EXCEPTION 'Cannot deduplicate exam version %: package checksums differ for % and %', conflict.version_id, conflict.keeper_id, conflict.removed_id; END IF; " +
            "END $$;");

        // PublicationTarget has no database foreign key, but its package id is a live semantic
        // reference used by sync and summary queries. Repoint those references before deleting
        // duplicate package rows so targets remain attached to the retained earliest package.
        migrationBuilder.Sql(
            "WITH ranked AS (" +
            " SELECT \"Id\", FIRST_VALUE(\"Id\") OVER (PARTITION BY \"ExamVersionId\" ORDER BY COALESCE(\"PublishedAt\", \"CreatedAt\"), \"Id\") AS keeper_id," +
            " ROW_NUMBER() OVER (PARTITION BY \"ExamVersionId\" ORDER BY COALESCE(\"PublishedAt\", \"CreatedAt\"), \"Id\") AS row_number" +
            " FROM publication.packages" +
            ") UPDATE publication.targets AS target" +
            " SET \"PublicationPackageId\" = ranked.keeper_id" +
            " FROM ranked WHERE target.\"PublicationPackageId\" = ranked.\"Id\" AND ranked.row_number > 1;");

        // Repointing can make two target rows identical; keep the earliest such row so sync does
        // not emit duplicate target scopes for the retained package.
        migrationBuilder.Sql(
            "WITH ranked AS (" +
            " SELECT \"Id\", ROW_NUMBER() OVER (PARTITION BY \"PublicationPackageId\", \"TargetType\", \"TargetId\" ORDER BY \"CreatedAt\", \"Id\") AS row_number" +
            " FROM publication.targets" +
            ") DELETE FROM publication.targets AS target" +
            " USING ranked WHERE target.\"Id\" = ranked.\"Id\" AND ranked.row_number > 1;");

        migrationBuilder.Sql(
            "WITH ranked AS (" +
            " SELECT \"Id\", FIRST_VALUE(\"Id\") OVER (PARTITION BY \"ExamVersionId\" ORDER BY COALESCE(\"PublishedAt\", \"CreatedAt\"), \"Id\") AS keeper_id, " +
            " ROW_NUMBER() OVER (PARTITION BY \"ExamVersionId\" ORDER BY COALESCE(\"PublishedAt\", \"CreatedAt\"), \"Id\") AS row_number " +
            " FROM publication.packages), moved AS (" +
            " INSERT INTO sync.cursors (\"Id\", \"NodeId\", \"CursorKey\", \"CursorValue\", \"UpdatedAt\") " +
            " SELECT 'cursor-' || gen_random_uuid()::text, cursor.\"NodeId\", 'package:' || ranked.keeper_id, cursor.\"CursorValue\", cursor.\"UpdatedAt\" " +
            " FROM ranked JOIN sync.cursors AS cursor ON cursor.\"CursorKey\" = 'package:' || ranked.\"Id\" " +
            " WHERE ranked.row_number > 1 ON CONFLICT (\"NodeId\", \"CursorKey\") DO NOTHING RETURNING 1) " +
            " DELETE FROM sync.cursors AS cursor USING ranked " +
            " WHERE ranked.row_number > 1 AND cursor.\"CursorKey\" = 'package:' || ranked.\"Id\";");

        migrationBuilder.Sql(
            "WITH ranked AS (" +
            " SELECT \"Id\", ROW_NUMBER() OVER (PARTITION BY \"ExamVersionId\" ORDER BY COALESCE(\"PublishedAt\", \"CreatedAt\"), \"Id\") AS row_number" +
            " FROM publication.packages" +
            ") DELETE FROM publication.packages AS package" +
            " USING ranked WHERE package.\"Id\" = ranked.\"Id\" AND ranked.row_number > 1;");

        migrationBuilder.CreateIndex(
            name: "IX_packages_ExamVersionId",
            schema: "publication",
            table: "packages",
            column: "ExamVersionId",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_packages_ExamVersionId",
            schema: "publication",
            table: "packages");

        // Preserve the role if it has been assigned to a user; role assignments are operational data.
        migrationBuilder.Sql(
            "DELETE FROM core.roles AS role WHERE role.\"Code\" = 'ExamAuthor' AND role.\"Id\" = 'role-exam-author'" +
            " AND NOT EXISTS (SELECT 1 FROM core.user_roles WHERE \"RoleId\" = role.\"Id\");");
    }
}
