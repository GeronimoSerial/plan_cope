using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PlanCope.Shared.Contracts.Exams;
using PlanCope.Shared.Domain.Central;

namespace PlanCope.Central.Api.Services;

public static class ExamPackageChecksum
{
    public static IReadOnlyList<string> GetReferencedImageAssetIds(IEnumerable<JsonElement> configs)
    {
        return configs
            .Where(static config => config.ValueKind == JsonValueKind.Object && config.TryGetProperty("imageAssetId", out _))
            .Select(static config => config.GetProperty("imageAssetId"))
            .Where(static value => value.ValueKind == JsonValueKind.String)
            .Select(static value => value.GetString())
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    public static string Compute(
        Exam exam,
        ExamVersion version,
        IReadOnlyList<BlockDto> blocks,
        IReadOnlyList<AnswerKeyDto> answerKeys,
        IReadOnlyList<PublishedAssetDto> assets,
        IReadOnlyList<PublicationTargetDto> targets,
        JsonElement? metadata)
    {
        var payload = JsonSerializer.Serialize(new
        {
            ExamId = exam.Id,
            exam.Code,
            exam.Title,
            VersionId = version.Id,
            version.VersionNumber,
            version.SchemaVersion,
            Metadata = metadata,
            Blocks = blocks,
            AnswerKeys = answerKeys,
            Assets = assets,
            Targets = targets
        });

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }
}
