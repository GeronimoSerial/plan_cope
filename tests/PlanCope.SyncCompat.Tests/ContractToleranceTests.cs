using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using PlanCope.Shared.Contracts.Auth;
using PlanCope.Shared.Contracts.Exams;
using PlanCope.Shared.Contracts.Serialization;
using PlanCope.Shared.Contracts.Sync;
using Xunit;

namespace PlanCope.SyncCompat.Tests;

/// <summary>
/// Pins cross-version tolerance for the DTOs exchanged between Central and Local.
/// Central and Local run different versions, offline, on their own update schedule:
/// a newer Central must be able to send a DTO with extra fields and a not-yet-updated
/// Local must still deserialize it without throwing, and a DTO that is missing a field
/// must degrade predictably rather than crash every school on its next sync.
/// </summary>
public sealed class ContractToleranceTests
{
    private static T Deserialize<T>(string json, JsonTypeInfo<T> typeInfo)
        => JsonSerializer.Deserialize<T>(json, typeInfo)!;

    [Fact]
    public void LoginResponse_ignores_unknown_extra_property()
    {
        const string json = """
            {
              "accessToken": "eyJhbGciOiJIUzI1NiJ9.payload.signature",
              "refreshToken": "refresh-tok-123456",
              "user": {
                "id": "u-42",
                "displayName": "Ana García",
                "role": "Teacher",
                "schoolId": "sch-7",
                "rosterScope": "school",
                "rosterCues": ["1800554-00", "1800554-01"]
              },
              "futureFieldNobodyKnowsYet": "ignored"
            }
            """;

        var deserialized = Deserialize(json, PlanCopeJsonSerializerContext.Default.LoginResponse);

        Assert.Equal("eyJhbGciOiJIUzI1NiJ9.payload.signature", deserialized.AccessToken);
        Assert.Equal("refresh-tok-123456", deserialized.RefreshToken);
        Assert.Equal("u-42", deserialized.User.Id);
        Assert.Equal("Ana García", deserialized.User.DisplayName);
        Assert.Equal("Teacher", deserialized.User.Role);
        Assert.Equal("sch-7", deserialized.User.SchoolId);
        Assert.Equal("school", deserialized.User.RosterScope);
        Assert.Equal(new[] { "1800554-00", "1800554-01" }, deserialized.User.RosterCues);
    }

    [Fact]
    public void LoginResponse_missing_access_token_becomes_null()
    {
        // Observed: deserialization does NOT throw. No JsonRequiredAttribute/required
        // keyword exists on this DTO, so a missing field silently becomes null rather
        // than throwing — this test pins that as the current contract, not as an accident.
        const string json = """
            {
              "refreshToken": "refresh-tok-123456",
              "user": {
                "id": "u-42",
                "displayName": "Ana García",
                "role": "Teacher",
                "schoolId": "sch-7",
                "rosterScope": "school",
                "rosterCues": ["1800554-00", "1800554-01"]
              }
            }
            """;

        var deserialized = Deserialize(json, PlanCopeJsonSerializerContext.Default.LoginResponse);

        Assert.Null(deserialized.AccessToken);
        Assert.Equal("refresh-tok-123456", deserialized.RefreshToken);
    }

    [Fact]
    public void PullResponse_ignores_unknown_extra_property()
    {
        const string json = """
            {
              "items": [
                {
                  "entityType": "exam",
                  "entityId": "ex-1",
                  "operation": "upsert",
                  "payload": { "foo": "bar" },
                  "updatedAt": "2026-09-15T10:00:00Z",
                  "checksum": "sha256-abc"
                }
              ],
              "nextCursor": "cursor-202",
              "hasMore": true,
              "checksums": { "ex-1": "sha256-abc" },
              "futureFieldNobodyKnowsYet": "ignored"
            }
            """;

        var deserialized = Deserialize(json, PlanCopeJsonSerializerContext.Default.PullResponse);

        var item = Assert.Single(deserialized.Items);
        Assert.Equal("exam", item.EntityType);
        Assert.Equal("ex-1", item.EntityId);
        Assert.Equal("upsert", item.Operation);
        Assert.Equal("2026-09-15T10:00:00Z", item.UpdatedAt);
        Assert.Equal("sha256-abc", item.Checksum);
        Assert.Equal("cursor-202", deserialized.NextCursor);
        Assert.True(deserialized.HasMore);
        Assert.Equal("sha256-abc", deserialized.Checksums["ex-1"]);
    }

    [Fact]
    public void PullResponse_missing_next_cursor_becomes_null()
    {
        // Observed: deserialization does NOT throw. No JsonRequiredAttribute/required
        // keyword exists on this DTO, so a missing field silently becomes null rather
        // than throwing — this test pins that as the current contract, not as an accident.
        const string json = """
            {
              "items": [],
              "hasMore": false,
              "checksums": {}
            }
            """;

        var deserialized = Deserialize(json, PlanCopeJsonSerializerContext.Default.PullResponse);

        Assert.Null(deserialized.NextCursor);
        Assert.Empty(deserialized.Items);
        Assert.False(deserialized.HasMore);
    }

    [Fact]
    public void ExamVersionDto_ignores_unknown_extra_property()
    {
        const string json = """
            {
              "id": "ev-101",
              "examId": "ex-9",
              "versionNumber": 3,
              "schemaVersion": 1,
              "status": "Approved",
              "metadata": { "author": "teacher01" },
              "blocks": [
                {
                  "id": "blk-1",
                  "versionId": "ev-101",
                  "orderIndex": 0,
                  "blockType": 2,
                  "title": "Pregunta 1",
                  "config": { "options": ["A", "B"], "answerIndex": 1 }
                }
              ],
              "answerKeys": [
                {
                  "id": "ak-1",
                  "blockId": "blk-1",
                  "correctAnswer": { "option": "B" },
                  "scoreValue": 1,
                  "metadata": { "explanation": "2+2=4" }
                }
              ],
              "assets": [],
              "futureFieldNobodyKnowsYet": "ignored"
            }
            """;

        var deserialized = Deserialize(json, PlanCopeJsonSerializerContext.Default.ExamVersionDto);

        Assert.Equal("ev-101", deserialized.Id);
        Assert.Equal("ex-9", deserialized.ExamId);
        Assert.Equal(3, deserialized.VersionNumber);
        Assert.Equal(1, deserialized.SchemaVersion);
        Assert.Equal("Approved", deserialized.Status);
        var block = Assert.Single(deserialized.Blocks);
        Assert.Equal("blk-1", block.Id);
        Assert.Equal(PlanCope.Shared.Domain.BlockType.MultipleChoice, block.BlockType);
        var answerKey = Assert.Single(deserialized.AnswerKeys);
        Assert.Equal("ak-1", answerKey.Id);
        Assert.Empty(deserialized.Assets);
    }

    [Fact]
    public void ExamVersionDto_missing_id_becomes_null()
    {
        // Observed: deserialization does NOT throw. No JsonRequiredAttribute/required
        // keyword exists on this DTO, so a missing field silently becomes null rather
        // than throwing — this test pins that as the current contract, not as an accident.
        const string json = """
            {
              "examId": "ex-9",
              "versionNumber": 3,
              "schemaVersion": 1,
              "status": "Approved",
              "metadata": { "author": "teacher01" },
              "blocks": [],
              "answerKeys": [],
              "assets": []
            }
            """;

        var deserialized = Deserialize(json, PlanCopeJsonSerializerContext.Default.ExamVersionDto);

        Assert.Null(deserialized.Id);
        Assert.Equal("ex-9", deserialized.ExamId);
        Assert.Equal(3, deserialized.VersionNumber);
    }
}