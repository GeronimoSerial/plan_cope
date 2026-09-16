using System.Text.Json;
using System.Text.Json.Serialization;
using PlanCope.Shared.Domain.Local;
using PlanCope.Shared.Grading;

namespace PlanCope.Local.Api.Data.Repositories;

/// <summary>
/// The grading outcome persisted atomically with an attempt submission. Status is
/// <c>"graded"</c> when the engine produced a full result, or <c>"ungradable"</c> when no
/// scoring policy could be resolved and the attempt must be re-graded later.
/// </summary>
public sealed record GradingOutcome(
    string Status,
    int GradingSchemaVersion,
    string? ScoringPolicy,
    double? Score,
    double? ScoreMax,
    string? BlocksJson,
    string GradedAt)
{
    public static GradingOutcome Graded(AttemptResult result, string gradedAt)
    {
        return new GradingOutcome(
            "graded",
            result.GradingSchemaVersion,
            result.ScoringPolicy?.ToString(),
            (double)result.Score,
            (double)result.ScoreMax,
            JsonSerializer.Serialize(result.Blocks, new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } }),
            gradedAt);
    }

    public static GradingOutcome Ungradable(string gradedAt)
    {
        return new GradingOutcome("ungradable", PlanCope.Shared.Grading.GradingSchemaVersion.Current, null, null, null, null, gradedAt);
    }
}

public interface IAttemptRepository
{
    Task CreateResolutionAsync(StudentResolution resolution, CancellationToken cancellationToken = default);

    Task<NominalAttemptStartResult> StartNominalAttemptAsync(
        string deliverySessionId,
        string tokenHash,
        string startedAt,
        CancellationToken cancellationToken = default);

    Task CreateAsync(StudentAttempt attempt, CancellationToken cancellationToken = default);

    Task<StudentAttempt?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    Task<bool> ExistsForStudentAsync(string deliverySessionId, string studentCode, CancellationToken cancellationToken = default);

    Task<int> GetNextLocalSequenceAsync(string deliverySessionId, CancellationToken cancellationToken = default);

    Task UpsertAnswersAsync(string attemptId, IReadOnlyList<SubmissionAnswer> answers, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubmissionAnswer>> GetAnswersAsync(string attemptId, CancellationToken cancellationToken = default);

    Task SubmitAsync(string id, string submittedAt, string confirmationCode, CancellationToken cancellationToken = default);

    Task<bool> SubmitWithOutboxAsync(
        string id,
        string submittedAt,
        string confirmationCode,
        SyncOutbox outbox,
        GradingOutcome gradingOutcome,
        CancellationToken cancellationToken = default);
}

public sealed record StudentResolution(
    string Id,
    string DeliverySessionId,
    string RosterSnapshotId,
    string RosterSectionId,
    string RosterStudentId,
    int GePersonId,
    string FirstName,
    string LastName,
    string DocumentLast4,
    string TokenHash,
    string ExpiresAt,
    string CreatedAt);

public enum NominalAttemptStartStatus
{
    Started,
    ResolutionNotFound,
    ResolutionExpired,
    ResolutionUsed,
    AttemptExists
}

public sealed record NominalAttemptStartResult(NominalAttemptStartStatus Status, StudentAttempt? Attempt)
{
    public static NominalAttemptStartResult Started(StudentAttempt attempt) => new(NominalAttemptStartStatus.Started, attempt);
}
