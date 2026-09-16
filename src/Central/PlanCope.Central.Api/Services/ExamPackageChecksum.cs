using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PlanCope.Shared.Contracts.Exams;
using PlanCope.Shared.Domain.Central;

namespace PlanCope.Central.Api.Services;

public static class ExamPackageChecksum
{
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
            version.ScoringPolicy,
            Metadata = metadata,
            Blocks = blocks,
            AnswerKeys = answerKeys,
            Assets = assets,
            Targets = targets
        });

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }
}