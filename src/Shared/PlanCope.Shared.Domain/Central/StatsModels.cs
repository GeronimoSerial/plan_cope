namespace PlanCope.Shared.Domain.Central;

public sealed record ExamRollup(
    string Id,
    string Cue,
    string SchoolYear,
    string Course,
    string ExamVersionId,
    int AttemptCount,
    double ScoreSum,
    double ScoreMaxSum,
    DateTimeOffset UpdatedAt);

public sealed record ExamRollupBlock(
    string Id,
    string RollupId,
    string BlockId,
    int CorrectCount,
    int PartialCount,
    int IncorrectCount,
    int BlankCount,
    int UngradableCount,
    double ScoreSum,
    double ScoreMaxSum);
