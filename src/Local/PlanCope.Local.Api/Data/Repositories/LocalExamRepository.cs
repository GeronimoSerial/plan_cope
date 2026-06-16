using Dapper;
using PlanCope.Shared.Domain.Local;

namespace PlanCope.Local.Api.Data.Repositories;

public sealed class LocalExamRepository(ILocalSqliteConnectionFactory connectionFactory) : ILocalExamRepository
{
    public async Task<IReadOnlyList<LocalExamVersion>> GetExamsAsync(string? grade = null, CancellationToken cancellationToken = default)
    {
        var sql = """
            SELECT id, remote_exam_version_id, exam_code, version_number, checksum, metadata_json, schema_version, synced_at
            FROM local_exam_versions
            """;

        object? parameters = null;

        if (!string.IsNullOrWhiteSpace(grade))
        {
            sql += """

                WHERE json_extract(metadata_json, '$.grade') = @Grade
                """;
            parameters = new { Grade = grade };
        }

        sql += """

            ORDER BY exam_code, version_number DESC;
            """;

        using var connection = connectionFactory.CreateOpenConnection();
        var rows = await connection.QueryAsync<LocalExamVersionRow>(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        return rows.Select(static row => row.ToDomain()).ToList();
    }

    public async Task<LocalExamVersion?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, remote_exam_version_id, exam_code, version_number, checksum, metadata_json, schema_version, synced_at
            FROM local_exam_versions
            WHERE id = @Id
            LIMIT 1;
            """;

        using var connection = connectionFactory.CreateOpenConnection();
        var row = await connection.QuerySingleOrDefaultAsync<LocalExamVersionRow>(new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));
        return row?.ToDomain();
    }

    public async Task<IReadOnlyList<LocalExamBlock>> GetBlocksAsync(string localExamVersionId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, local_exam_version_id, remote_block_id, order_index, block_type, config_json, validation_json
            FROM local_exam_blocks
            WHERE local_exam_version_id = @LocalExamVersionId
            ORDER BY order_index;
            """;

        using var connection = connectionFactory.CreateOpenConnection();
        var rows = await connection.QueryAsync<LocalExamBlockRow>(new CommandDefinition(sql, new { LocalExamVersionId = localExamVersionId }, cancellationToken: cancellationToken));
        return rows.Select(static row => row.ToDomain()).ToList();
    }

    private sealed class LocalExamVersionRow
    {
        public string Id { get; init; } = string.Empty;
        public string RemoteExamVersionId { get; init; } = string.Empty;
        public string ExamCode { get; init; } = string.Empty;
        public long VersionNumber { get; init; }
        public string Checksum { get; init; } = string.Empty;
        public string? MetadataJson { get; init; }
        public long SchemaVersion { get; init; }
        public string SyncedAt { get; init; } = string.Empty;

        public LocalExamVersion ToDomain()
        {
            return new LocalExamVersion(Id, RemoteExamVersionId, ExamCode, checked((int)VersionNumber), Checksum, MetadataJson, checked((int)SchemaVersion), SyncedAt);
        }
    }

    private sealed class LocalExamBlockRow
    {
        public string Id { get; init; } = string.Empty;
        public string LocalExamVersionId { get; init; } = string.Empty;
        public string RemoteBlockId { get; init; } = string.Empty;
        public long OrderIndex { get; init; }
        public string BlockType { get; init; } = string.Empty;
        public string ConfigJson { get; init; } = string.Empty;
        public string? ValidationJson { get; init; }

        public LocalExamBlock ToDomain()
        {
            return new LocalExamBlock(Id, LocalExamVersionId, RemoteBlockId, checked((int)OrderIndex), ParseBlockType(BlockType), ConfigJson, ValidationJson);
        }

        private static Shared.Domain.BlockType ParseBlockType(string blockType)
        {
            return blockType switch
            {
                "text" => Shared.Domain.BlockType.Text,
                "image" => Shared.Domain.BlockType.Image,
                "multiple_choice" => Shared.Domain.BlockType.MultipleChoice,
                "true_false" => Shared.Domain.BlockType.TrueFalse,
                "short_answer" => Shared.Domain.BlockType.ShortAnswer,
                _ => Enum.Parse<Shared.Domain.BlockType>(blockType, ignoreCase: true)
            };
        }
    }
}
