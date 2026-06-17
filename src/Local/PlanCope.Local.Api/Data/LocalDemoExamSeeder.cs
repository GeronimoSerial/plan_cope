using Dapper;

namespace PlanCope.Local.Api.Data;

public sealed class LocalDemoExamSeeder(ILocalSqliteConnectionFactory connectionFactory)
{
    public void SeedIfEmpty()
    {
        using var connection = connectionFactory.CreateOpenConnection();
        var count = connection.ExecuteScalar<long>("SELECT COUNT(*) FROM local_exam_versions;");

        if (count > 0)
        {
            return;
        }

        const string examId = "demo-matematica-6-v1";
        var now = DateTimeOffset.UtcNow.ToString("O");

        using var transaction = connection.BeginTransaction();
        connection.Execute(
            """
            INSERT INTO local_exam_versions (id, remote_exam_version_id, exam_code, version_number, checksum, metadata_json, schema_version, synced_at)
            VALUES (@Id, @RemoteExamVersionId, @ExamCode, @VersionNumber, @Checksum, @MetadataJson, @SchemaVersion, @SyncedAt);
            """,
            new
            {
                Id = examId,
                RemoteExamVersionId = "demo-remote-matematica-6-v1",
                ExamCode = "MAT-6-DEMO",
                VersionNumber = 1,
                Checksum = "demo",
                MetadataJson = """{"title":"Matematica 6 - Demo","grade":"6","division":"A","subject":"Matematica","fallback":true}""",
                SchemaVersion = 1,
                SyncedAt = now
            },
            transaction);

        var blocks = new[]
        {
            new
            {
                Id = "demo-mat-6-bienvenida",
                RemoteBlockId = "demo-remote-mat-6-bienvenida",
                OrderIndex = 0,
                BlockType = "text",
                ConfigJson = """{"content":"Lee cada consigna con atencion. Puedes revisar tus respuestas antes de enviar."}"""
            },
            new
            {
                Id = "demo-mat-6-multiple-1",
                RemoteBlockId = "demo-remote-mat-6-multiple-1",
                OrderIndex = 1,
                BlockType = "multiple_choice",
                ConfigJson = """{"question":"Cuanto es 18 + 24?","options":[{"value":"38","label":"38"},{"value":"42","label":"42"},{"value":"44","label":"44"}]}"""
            },
            new
            {
                Id = "demo-mat-6-truefalse-1",
                RemoteBlockId = "demo-remote-mat-6-truefalse-1",
                OrderIndex = 2,
                BlockType = "true_false",
                ConfigJson = """{"question":"El numero 9 es multiplo de 3."}"""
            },
            new
            {
                Id = "demo-mat-6-short-1",
                RemoteBlockId = "demo-remote-mat-6-short-1",
                OrderIndex = 3,
                BlockType = "short_answer",
                ConfigJson = """{"prompt":"Explica brevemente como resolverias 15 x 4."}"""
            }
        };

        foreach (var block in blocks)
        {
            connection.Execute(
                """
                INSERT INTO local_exam_blocks (id, local_exam_version_id, remote_block_id, order_index, block_type, config_json, validation_json)
                VALUES (@Id, @LocalExamVersionId, @RemoteBlockId, @OrderIndex, @BlockType, @ConfigJson, NULL);
                """,
                new
                {
                    block.Id,
                    LocalExamVersionId = examId,
                    block.RemoteBlockId,
                    block.OrderIndex,
                    block.BlockType,
                    block.ConfigJson
                },
                transaction);
        }

        transaction.Commit();
    }
}
