using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PlanCope.Central.Api.Data;

#nullable disable

namespace PlanCope.Central.Migrations.Migrations;

[DbContext(typeof(PlanCopeDbContext))]
[Migration("20260930120000_RemoveUnsupportedExamBlockTypes")]
public sealed class RemoveUnsupportedExamBlockTypes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "DELETE FROM publication.targets WHERE \"PublicationPackageId\" IN " +
            "(SELECT package.\"Id\" FROM publication.packages AS package JOIN exam.blocks AS block " +
            "ON block.\"ExamVersionId\" = package.\"ExamVersionId\" " +
            "WHERE block.\"BlockType\" IN ('Text', 'Image', 'ShortAnswer')); " +
            "DELETE FROM sync.cursors WHERE \"CursorKey\" IN " +
            "(SELECT 'package:' || package.\"Id\" FROM publication.packages AS package JOIN exam.blocks AS block " +
            "ON block.\"ExamVersionId\" = package.\"ExamVersionId\" " +
            "WHERE block.\"BlockType\" IN ('Text', 'Image', 'ShortAnswer')); " +
            "DELETE FROM publication.packages WHERE \"ExamVersionId\" IN " +
            "(SELECT DISTINCT \"ExamVersionId\" FROM exam.blocks " +
            "WHERE \"BlockType\" IN ('Text', 'Image', 'ShortAnswer')); " +
            "DELETE FROM exam.asset_usages WHERE \"ExamBlockId\" IN " +
            "(SELECT \"Id\" FROM exam.blocks WHERE \"BlockType\" IN ('Text', 'Image', 'ShortAnswer')); " +
            "DELETE FROM exam.answer_keys WHERE \"ExamBlockId\" IN " +
            "(SELECT \"Id\" FROM exam.blocks WHERE \"BlockType\" IN ('Text', 'Image', 'ShortAnswer')); " +
            "DELETE FROM exam.block_options WHERE \"ExamBlockId\" IN " +
            "(SELECT \"Id\" FROM exam.blocks WHERE \"BlockType\" IN ('Text', 'Image', 'ShortAnswer')); " +
            "DELETE FROM exam.blocks WHERE \"BlockType\" IN ('Text', 'Image', 'ShortAnswer');");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Deleted block content cannot be reconstructed.
    }
}
