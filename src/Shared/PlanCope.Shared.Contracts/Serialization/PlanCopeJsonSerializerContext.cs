using System.Text.Json.Serialization;
using PlanCope.Shared.Contracts.Auth;
using PlanCope.Shared.Contracts.Exams;
using PlanCope.Shared.Contracts.Local;
using PlanCope.Shared.Contracts.Sync;

namespace PlanCope.Shared.Contracts.Serialization;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(LoginResponse))]
[JsonSerializable(typeof(RefreshTokenRequest))]
[JsonSerializable(typeof(UserProfileDto))]
[JsonSerializable(typeof(ExamSummaryDto))]
[JsonSerializable(typeof(ExamVersionDto))]
[JsonSerializable(typeof(BlockDto))]
[JsonSerializable(typeof(AnswerKeyDto))]
[JsonSerializable(typeof(AssetDto))]
[JsonSerializable(typeof(CreateExamRequest))]
[JsonSerializable(typeof(CreateExamVersionRequest))]
[JsonSerializable(typeof(UpsertBlockRequest))]
[JsonSerializable(typeof(CreateSessionRequest))]
[JsonSerializable(typeof(UpdateSessionStatusRequest))]
[JsonSerializable(typeof(StartAttemptRequest))]
[JsonSerializable(typeof(SaveAnswersRequest))]
[JsonSerializable(typeof(SubmissionAnswerDto))]
[JsonSerializable(typeof(SubmitAttemptResponse))]
[JsonSerializable(typeof(PullRequest))]
[JsonSerializable(typeof(SyncItem))]
[JsonSerializable(typeof(PullResponse))]
[JsonSerializable(typeof(PushItem))]
[JsonSerializable(typeof(PushRequest))]
[JsonSerializable(typeof(PushItemResult))]
[JsonSerializable(typeof(PushResponse))]
public sealed partial class PlanCopeJsonSerializerContext : JsonSerializerContext;
