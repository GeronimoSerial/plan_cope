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

    public async Task<LocalAsset?> GetAssetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, remote_asset_id, local_exam_version_id, file_name, mime_type, checksum, local_path, synced_at
            FROM local_assets
            WHERE id = @Id
            LIMIT 1;
            """;

        using var connection = connectionFactory.CreateOpenConnection();
        var row = await connection.QuerySingleOrDefaultAsync<LocalAssetRow>(new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));
        return row?.ToDomain();
    }

    public async Task UpsertImportedExamAsync(
        LocalExamVersion exam,
        IReadOnlyList<LocalExamBlock> blocks,
        IReadOnlyList<LocalAsset> assets,
        IReadOnlyList<LocalAnswerKey> answerKeys,
        CancellationToken cancellationToken = default)
    {
        const string upsertExamSql = """
            INSERT INTO local_exam_versions (id, remote_exam_version_id, exam_code, version_number, checksum, metadata_json, schema_version, synced_at)
            VALUES (@Id, @RemoteExamVersionId, @ExamCode, @VersionNumber, @Checksum, @MetadataJson, @SchemaVersion, @SyncedAt)
            ON CONFLICT(id) DO UPDATE SET
                remote_exam_version_id = excluded.remote_exam_version_id,
                exam_code = excluded.exam_code,
                version_number = excluded.version_number,
                checksum = excluded.checksum,
                metadata_json = excluded.metadata_json,
                schema_version = excluded.schema_version,
                synced_at = excluded.synced_at;
            """;

        const string upsertBlockSql = """
            INSERT INTO local_exam_blocks (id, local_exam_version_id, remote_block_id, order_index, block_type, config_json, validation_json)
            VALUES (@Id, @LocalExamVersionId, @RemoteBlockId, @OrderIndex, @BlockType, @ConfigJson, @ValidationJson)
            ON CONFLICT(id) DO UPDATE SET
                local_exam_version_id = excluded.local_exam_version_id,
                remote_block_id = excluded.remote_block_id,
                order_index = excluded.order_index,
                block_type = excluded.block_type,
                config_json = excluded.config_json,
                validation_json = excluded.validation_json;
            """;

        const string upsertAssetSql = """
            INSERT INTO local_assets (id, remote_asset_id, local_exam_version_id, file_name, mime_type, checksum, local_path, synced_at)
            VALUES (@Id, @RemoteAssetId, @LocalExamVersionId, @FileName, @MimeType, @Checksum, @LocalPath, @SyncedAt)
            ON CONFLICT(id) DO UPDATE SET
                remote_asset_id = excluded.remote_asset_id,
                local_exam_version_id = excluded.local_exam_version_id,
                file_name = excluded.file_name,
                mime_type = excluded.mime_type,
                checksum = excluded.checksum,
                local_path = excluded.local_path,
                synced_at = excluded.synced_at;
            """;

        const string upsertAnswerKeySql = """
            INSERT INTO local_answer_keys (id, local_exam_version_id, remote_block_id, correct_answer_json, score_value)
            VALUES (@Id, @LocalExamVersionId, @RemoteBlockId, @CorrectAnswerJson, @ScoreValue)
            ON CONFLICT(id) DO UPDATE SET
                local_exam_version_id = excluded.local_exam_version_id,
                remote_block_id = excluded.remote_block_id,
                correct_answer_json = excluded.correct_answer_json,
                score_value = excluded.score_value;
            """;

        using var connection = connectionFactory.CreateOpenConnection();
        using var transaction = connection.BeginTransaction();

        await connection.ExecuteAsync(new CommandDefinition(upsertExamSql, exam, transaction, cancellationToken: cancellationToken));

        await DeleteMissingAsync(connection, transaction, "local_exam_blocks", "id", "local_exam_version_id", exam.Id, blocks.Select(static x => x.Id), cancellationToken);
        await DeleteMissingAsync(connection, transaction, "local_assets", "id", "local_exam_version_id", exam.Id, assets.Select(static x => x.Id), cancellationToken);
        await DeleteMissingAsync(connection, transaction, "local_answer_keys", "id", "local_exam_version_id", exam.Id, answerKeys.Select(static x => x.Id), cancellationToken);

        foreach (var block in blocks)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                upsertBlockSql,
                new
                {
                    block.Id,
                    block.LocalExamVersionId,
                    block.RemoteBlockId,
                    block.OrderIndex,
                    BlockType = ToStorageBlockType(block.BlockType),
                    block.ConfigJson,
                    block.ValidationJson
                },
                transaction,
                cancellationToken: cancellationToken));
        }

        foreach (var asset in assets)
        {
            await connection.ExecuteAsync(new CommandDefinition(upsertAssetSql, asset, transaction, cancellationToken: cancellationToken));
        }

        foreach (var answerKey in answerKeys)
        {
            await connection.ExecuteAsync(new CommandDefinition(upsertAnswerKeySql, answerKey, transaction, cancellationToken: cancellationToken));
        }

        transaction.Commit();
    }

    private static async Task DeleteMissingAsync(
        System.Data.IDbConnection connection,
        System.Data.IDbTransaction transaction,
        string table,
        string keyColumn,
        string parentColumn,
        string parentId,
        IEnumerable<string> keptIds,
        CancellationToken cancellationToken)
    {
        var ids = keptIds.ToArray();
        if (ids.Length == 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                $"DELETE FROM {table} WHERE {parentColumn} = @ParentId;",
                new { ParentId = parentId },
                transaction,
                cancellationToken: cancellationToken));
            return;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            $"DELETE FROM {table} WHERE {parentColumn} = @ParentId AND {keyColumn} NOT IN @Ids;",
            new { ParentId = parentId, Ids = ids },
            transaction,
            cancellationToken: cancellationToken));
    }

    private static string ToStorageBlockType(Shared.Domain.BlockType blockType)
    {
        return blockType switch
        {
            Shared.Domain.BlockType.Text => "text",
            Shared.Domain.BlockType.Image => "image",
            Shared.Domain.BlockType.MultipleChoice => "multiple_choice",
            Shared.Domain.BlockType.TrueFalse => "true_false",
            Shared.Domain.BlockType.ShortAnswer => "short_answer",
            _ => blockType.ToString()
        };
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

    private sealed class LocalAssetRow
    {
        public string Id { get; init; } = string.Empty;
        public string RemoteAssetId { get; init; } = string.Empty;
        public string LocalExamVersionId { get; init; } = string.Empty;
        public string FileName { get; init; } = string.Empty;
        public string MimeType { get; init; } = string.Empty;
        public string Checksum { get; init; } = string.Empty;
        public string LocalPath { get; init; } = string.Empty;
        public string SyncedAt { get; init; } = string.Empty;

        public LocalAsset ToDomain()
        {
            return new LocalAsset(Id, RemoteAssetId, LocalExamVersionId, FileName, MimeType, Checksum, LocalPath, SyncedAt);
        }
    }
}
