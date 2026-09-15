using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using PlanCope.Shared.Contracts.Auth;
using PlanCope.Shared.Contracts.Local;
using PlanCope.Shared.Contracts.Serialization;
using Xunit;

namespace PlanCope.SyncCompat.Tests;

public sealed class AuthAndLocalContractTests
{
    private static JsonElement JsonElementOf(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static string Serialize<T>(T value, JsonTypeInfo<T> typeInfo)
        => JsonSerializer.Serialize(value, typeInfo);

    private static T Deserialize<T>(string json, JsonTypeInfo<T> typeInfo)
        => JsonSerializer.Deserialize<T>(json, typeInfo)!;

    private static void AssertCanonicalRoundTrip<T>(T sample, JsonTypeInfo<T> typeInfo)
    {
        var json = Serialize(sample, typeInfo);
        var deserialized = Deserialize(json, typeInfo);
        Assert.Equal(json, Serialize(deserialized, typeInfo));
    }

    private static void AssertHasProperty(JsonElement root, string camelCaseName)
    {
        Assert.True(
            root.TryGetProperty(camelCaseName, out _),
            $"Expected property '{camelCaseName}' in serialized JSON: {root}");
    }

    [Fact]
    public void LoginRequest_round_trips_through_source_generated_context()
    {
        var sample = new LoginRequest("teacher01", "correct-horse-battery-staple");

        var json = Serialize(sample, PlanCopeJsonSerializerContext.Default.LoginRequest);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        AssertHasProperty(root, "username");
        AssertHasProperty(root, "password");

        Assert.Equal(sample, Deserialize(json, PlanCopeJsonSerializerContext.Default.LoginRequest));
    }

    [Fact]
    public void LoginResponse_round_trips_through_source_generated_context()
    {
        var sample = new LoginResponse(
            "eyJhbGciOiJIUzI1NiJ9.payload.signature",
            "refresh-tok-123456",
            new UserProfileDto(
                "u-42",
                "Ana García",
                "Teacher",
                "sch-7",
                "school",
                new[] { "1800554-00", "1800554-01" }));

        var json = Serialize(sample, PlanCopeJsonSerializerContext.Default.LoginResponse);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        AssertHasProperty(root, "accessToken");
        AssertHasProperty(root, "refreshToken");
        AssertHasProperty(root, "user");

        AssertCanonicalRoundTrip(sample, PlanCopeJsonSerializerContext.Default.LoginResponse);
    }

    [Fact]
    public void RefreshTokenRequest_round_trips_through_source_generated_context()
    {
        var sample = new RefreshTokenRequest("refresh-tok-abcdef");

        var json = Serialize(sample, PlanCopeJsonSerializerContext.Default.RefreshTokenRequest);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        AssertHasProperty(root, "refreshToken");

        Assert.Equal(sample, Deserialize(json, PlanCopeJsonSerializerContext.Default.RefreshTokenRequest));
    }

    [Fact]
    public void UserProfileDto_round_trips_through_source_generated_context()
    {
        var sample = new UserProfileDto(
            "u-9",
            "Carlos Pérez",
            "Admin",
            "sch-12",
            "all",
            new[] { "1800554-00", "1800554-99", "1700000-01" });

        var json = Serialize(sample, PlanCopeJsonSerializerContext.Default.UserProfileDto);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        AssertHasProperty(root, "id");
        AssertHasProperty(root, "displayName");
        AssertHasProperty(root, "role");
        AssertHasProperty(root, "schoolId");
        AssertHasProperty(root, "rosterScope");
        AssertHasProperty(root, "rosterCues");

        AssertCanonicalRoundTrip(sample, PlanCopeJsonSerializerContext.Default.UserProfileDto);
    }

    [Fact]
    public void CreateSessionRequest_round_trips_through_source_generated_context()
    {
        var sample = new CreateSessionRequest(
            "ev-101",
            "SCHL-001",
            "CLS-A7",
            "COM-3",
            "teacher01",
            28,
            JsonElementOf("""{"durationMinutes":120,"secure":true}"""),
            "2026",
            "rs-9",
            "sec-2");

        var json = Serialize(sample, PlanCopeJsonSerializerContext.Default.CreateSessionRequest);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        AssertHasProperty(root, "examVersionId");
        AssertHasProperty(root, "schoolCode");
        AssertHasProperty(root, "classroomCode");
        AssertHasProperty(root, "commissionCode");
        AssertHasProperty(root, "startedBy");
        AssertHasProperty(root, "expectedStudentCount");
        AssertHasProperty(root, "config");
        AssertHasProperty(root, "schoolYear");
        AssertHasProperty(root, "rosterSnapshotId");
        AssertHasProperty(root, "rosterSectionId");

        AssertCanonicalRoundTrip(sample, PlanCopeJsonSerializerContext.Default.CreateSessionRequest);
    }

    [Fact]
    public void UpdateSessionStatusRequest_round_trips_through_source_generated_context()
    {
        var sample = new UpdateSessionStatusRequest("InProgress");

        var json = Serialize(sample, PlanCopeJsonSerializerContext.Default.UpdateSessionStatusRequest);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        AssertHasProperty(root, "status");

        Assert.Equal(sample, Deserialize(json, PlanCopeJsonSerializerContext.Default.UpdateSessionStatusRequest));
    }

    [Fact]
    public void ResolveStudentRequest_round_trips_through_source_generated_context()
    {
        var sample = new ResolveStudentRequest("40787654");

        var json = Serialize(sample, PlanCopeJsonSerializerContext.Default.ResolveStudentRequest);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        AssertHasProperty(root, "document");

        Assert.Equal(sample, Deserialize(json, PlanCopeJsonSerializerContext.Default.ResolveStudentRequest));
    }

    [Fact]
    public void ResolvedStudentDto_round_trips_through_source_generated_context()
    {
        var sample = new ResolvedStudentDto(
            "María Rodríguez",
            "40.787.654",
            "María",
            "Rodríguez");

        var json = Serialize(sample, PlanCopeJsonSerializerContext.Default.ResolvedStudentDto);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        AssertHasProperty(root, "displayName");
        AssertHasProperty(root, "maskedDocument");
        AssertHasProperty(root, "firstName");
        AssertHasProperty(root, "lastName");

        Assert.Equal(sample, Deserialize(json, PlanCopeJsonSerializerContext.Default.ResolvedStudentDto));
    }

    [Fact]
    public void ResolveStudentResponse_round_trips_through_source_generated_context()
    {
        var sample = new ResolveStudentResponse(
            "res-tok-77",
            new ResolvedStudentDto(
                "María Rodríguez",
                "40.787.654",
                "María",
                "Rodríguez"),
            "2026-09-15T14:30:00Z");

        var json = Serialize(sample, PlanCopeJsonSerializerContext.Default.ResolveStudentResponse);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        AssertHasProperty(root, "resolutionToken");
        AssertHasProperty(root, "student");
        AssertHasProperty(root, "expiresAt");

        Assert.Equal(sample, Deserialize(json, PlanCopeJsonSerializerContext.Default.ResolveStudentResponse));
    }

    [Fact]
    public void StartAttemptRequest_round_trips_through_source_generated_context()
    {
        var sample = new StartAttemptRequest("STU-901", "res-tok-xyz");

        var json = Serialize(sample, PlanCopeJsonSerializerContext.Default.StartAttemptRequest);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        AssertHasProperty(root, "studentCode");
        AssertHasProperty(root, "resolutionToken");

        Assert.Equal(sample, Deserialize(json, PlanCopeJsonSerializerContext.Default.StartAttemptRequest));
    }

    [Fact]
    public void SaveAnswersRequest_round_trips_through_source_generated_context()
    {
        var sample = new SaveAnswersRequest(new[]
        {
            new SubmissionAnswerDto("blk-1", JsonElementOf("""{"option":"A"}""")),
            new SubmissionAnswerDto("blk-2", JsonElementOf("""{"text":"primera respuesta"}""")),
        });

        var json = Serialize(sample, PlanCopeJsonSerializerContext.Default.SaveAnswersRequest);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        AssertHasProperty(root, "answers");

        AssertCanonicalRoundTrip(sample, PlanCopeJsonSerializerContext.Default.SaveAnswersRequest);
    }

    [Fact]
    public void SubmissionAnswerDto_round_trips_through_source_generated_context()
    {
        var sample = new SubmissionAnswerDto("blk-17", JsonElementOf("""{"choice":"C"}"""));

        var json = Serialize(sample, PlanCopeJsonSerializerContext.Default.SubmissionAnswerDto);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        AssertHasProperty(root, "blockId");
        AssertHasProperty(root, "answer");

        AssertCanonicalRoundTrip(sample, PlanCopeJsonSerializerContext.Default.SubmissionAnswerDto);
    }

    [Fact]
    public void SubmitAttemptResponse_round_trips_through_source_generated_context()
    {
        var sample = new SubmitAttemptResponse(
            "at-5001",
            "CONF-2026-0001",
            "2026-09-15T15:02:11Z");

        var json = Serialize(sample, PlanCopeJsonSerializerContext.Default.SubmitAttemptResponse);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        AssertHasProperty(root, "attemptId");
        AssertHasProperty(root, "confirmationCode");
        AssertHasProperty(root, "submittedAt");

        Assert.Equal(sample, Deserialize(json, PlanCopeJsonSerializerContext.Default.SubmitAttemptResponse));
    }
}