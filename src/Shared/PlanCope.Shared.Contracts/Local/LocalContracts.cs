using System.Text.Json;

namespace PlanCope.Shared.Contracts.Local;

public sealed record CreateSessionRequest(string ExamVersionId, string SchoolCode, string? ClassroomCode, string? CommissionCode, string StartedBy, int ExpectedStudentCount, JsonElement? Config);

public sealed record UpdateSessionStatusRequest(string Status);

public sealed record StartAttemptRequest(string StudentCode);

public sealed record SaveAnswersRequest(IReadOnlyList<SubmissionAnswerDto> Answers);

public sealed record SubmissionAnswerDto(string BlockId, JsonElement Answer);

public sealed record SubmitAttemptResponse(string AttemptId, string ConfirmationCode, string SubmittedAt);
