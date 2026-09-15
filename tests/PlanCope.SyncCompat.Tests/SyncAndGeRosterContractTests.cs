using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using PlanCope.Shared.Contracts.Serialization;
using PlanCope.Shared.Contracts.Sync;
using Xunit;

namespace PlanCope.SyncCompat.Tests;

public sealed class SyncAndGeRosterContractTests
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

    private static JsonElement FirstElement(JsonElement array)
    {
        Assert.Equal(JsonValueKind.Array, array.ValueKind);
        foreach (var element in array.EnumerateArray())
        {
            return element;
        }

        throw new Xunit.Sdk.XunitException("Expected non-empty array in serialized JSON.");
    }

    [Fact]
    public void PullRequest_round_trips_through_source_generated_context()
    {
        var sample = new PullRequest("nd-01", "cursor-abc-123", 100);

        var json = Serialize(sample, PlanCopeJsonSerializerContext.Default.PullRequest);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        AssertHasProperty(root, "nodeId");
        AssertHasProperty(root, "cursor");
        AssertHasProperty(root, "limit");

        Assert.Equal(sample, Deserialize(json, PlanCopeJsonSerializerContext.Default.PullRequest));
    }

    [Fact]
    public void SyncItem_round_trips_through_source_generated_context()
    {
        var sample = new SyncItem(
            "exam",
            "ev-101",
            "upsert",
            JsonElementOf("""{"title":"Final de Matemática"}"""),
            "2026-09-15T12:00:00Z",
            "sha256-checksum-1");

        var json = Serialize(sample, PlanCopeJsonSerializerContext.Default.SyncItem);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        AssertHasProperty(root, "entityType");
        AssertHasProperty(root, "entityId");
        AssertHasProperty(root, "operation");
        AssertHasProperty(root, "payload");
        AssertHasProperty(root, "updatedAt");
        AssertHasProperty(root, "checksum");

        AssertCanonicalRoundTrip(sample, PlanCopeJsonSerializerContext.Default.SyncItem);
    }

    [Fact]
    public void PullResponse_round_trips_through_source_generated_context()
    {
        var item = new SyncItem(
            "exam",
            "ev-101",
            "upsert",
            JsonElementOf("""{"title":"Final de Matemática"}"""),
            "2026-09-15T12:00:00Z",
            "sha256-checksum-1");
        var sample = new PullResponse(
            new[] { item },
            "cursor-next-456",
            true,
            new Dictionary<string, string>
            {
                ["ev-101"] = "sha256-checksum-1",
                ["blk-7"] = "sha256-checksum-2",
            });

        var json = Serialize(sample, PlanCopeJsonSerializerContext.Default.PullResponse);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        AssertHasProperty(root, "items");
        AssertHasProperty(root, "nextCursor");
        AssertHasProperty(root, "hasMore");
        AssertHasProperty(root, "checksums");

        var nested = FirstElement(root.GetProperty("items"));
        AssertHasProperty(nested, "entityType");
        AssertHasProperty(nested, "entityId");
        AssertHasProperty(nested, "operation");
        AssertHasProperty(nested, "payload");
        AssertHasProperty(nested, "updatedAt");
        AssertHasProperty(nested, "checksum");

        AssertCanonicalRoundTrip(sample, PlanCopeJsonSerializerContext.Default.PullResponse);
    }

    [Fact]
    public void PushItem_round_trips_through_source_generated_context()
    {
        var sample = new PushItem(
            "idem-0001",
            "exam_published",
            "exam",
            "ev-101",
            JsonElementOf("""{"versionId":"ev-101"}"""),
            "sha256-checksum-3",
            "2026-09-15T12:05:00Z");

        var json = Serialize(sample, PlanCopeJsonSerializerContext.Default.PushItem);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        AssertHasProperty(root, "idempotencyKey");
        AssertHasProperty(root, "eventType");
        AssertHasProperty(root, "aggregateType");
        AssertHasProperty(root, "aggregateId");
        AssertHasProperty(root, "payload");
        AssertHasProperty(root, "checksum");
        AssertHasProperty(root, "occurredAt");

        AssertCanonicalRoundTrip(sample, PlanCopeJsonSerializerContext.Default.PushItem);
    }

    [Fact]
    public void PushRequest_round_trips_through_source_generated_context()
    {
        var item = new PushItem(
            "idem-0001",
            "exam_published",
            "exam",
            "ev-101",
            JsonElementOf("""{"versionId":"ev-101"}"""),
            "sha256-checksum-3",
            "2026-09-15T12:05:00Z");
        var sample = new PushRequest("nd-01", new[] { item });

        var json = Serialize(sample, PlanCopeJsonSerializerContext.Default.PushRequest);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        AssertHasProperty(root, "nodeId");
        AssertHasProperty(root, "items");

        var nested = FirstElement(root.GetProperty("items"));
        AssertHasProperty(nested, "idempotencyKey");
        AssertHasProperty(nested, "eventType");
        AssertHasProperty(nested, "aggregateType");
        AssertHasProperty(nested, "aggregateId");
        AssertHasProperty(nested, "payload");
        AssertHasProperty(nested, "checksum");
        AssertHasProperty(nested, "occurredAt");

        AssertCanonicalRoundTrip(sample, PlanCopeJsonSerializerContext.Default.PushRequest);
    }

    [Fact]
    public void PushItemResult_round_trips_through_source_generated_context()
    {
        var sample = new PushItemResult("idem-0001", "applied", null);

        var json = Serialize(sample, PlanCopeJsonSerializerContext.Default.PushItemResult);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        AssertHasProperty(root, "idempotencyKey");
        AssertHasProperty(root, "status");
        AssertHasProperty(root, "reason");

        Assert.Equal(sample, Deserialize(json, PlanCopeJsonSerializerContext.Default.PushItemResult));
    }

    [Fact]
    public void PushResponse_round_trips_through_source_generated_context()
    {
        var sample = new PushResponse(
            2,
            1,
            new[]
            {
                new PushItemResult("idem-0001", "applied", null),
                new PushItemResult("idem-0002", "failed", "duplicate idempotency key"),
            });

        var json = Serialize(sample, PlanCopeJsonSerializerContext.Default.PushResponse);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        AssertHasProperty(root, "received");
        AssertHasProperty(root, "failed");
        AssertHasProperty(root, "results");

        var nested = FirstElement(root.GetProperty("results"));
        AssertHasProperty(nested, "idempotencyKey");
        AssertHasProperty(nested, "status");
        AssertHasProperty(nested, "reason");

        AssertCanonicalRoundTrip(sample, PlanCopeJsonSerializerContext.Default.PushResponse);
    }

    [Fact]
    public void GeRosterPullRequest_round_trips_through_source_generated_context()
    {
        var sample = new GeRosterPullRequest("1.2026", "2026");

        var json = Serialize(sample, PlanCopeJsonSerializerContext.Default.GeRosterPullRequest);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        AssertHasProperty(root, "cue");
        AssertHasProperty(root, "schoolYear");

        Assert.Equal(sample, Deserialize(json, PlanCopeJsonSerializerContext.Default.GeRosterPullRequest));
    }

    [Fact]
    public void GeRosterPackageDto_round_trips_through_source_generated_context()
    {
        var student = new GeRosterStudentPackageDto("stu-9001", "sec-501", 87654321, "40787654", "María", "Rodríguez");
        var section = new GeRosterSectionPackageDto("sec-501", 1800554, "4° A", "División A", "Secundaria", "Mañana", new[] { student });
        var sample = new GeRosterPackageDto(
            "snap-2026-01",
            "1.2026",
            "2026",
            new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.FromHours(-3)),
            "a1b2c3d4e5f60718293a4b5c6d7e8f90",
            1,
            1,
            "Ready",
            new[] { section },
            "Colegio Nacional Nº 1");

        var json = Serialize(sample, PlanCopeJsonSerializerContext.Default.GeRosterPackageDto);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        AssertHasProperty(root, "snapshotId");
        AssertHasProperty(root, "cue");
        AssertHasProperty(root, "schoolYear");
        AssertHasProperty(root, "fetchedAt");
        AssertHasProperty(root, "checksum");
        AssertHasProperty(root, "sectionCount");
        AssertHasProperty(root, "studentCount");
        AssertHasProperty(root, "status");
        AssertHasProperty(root, "sections");
        AssertHasProperty(root, "schoolName");

        var nestedSection = FirstElement(root.GetProperty("sections"));
        AssertHasProperty(nestedSection, "id");
        AssertHasProperty(nestedSection, "geSectionId");
        AssertHasProperty(nestedSection, "course");
        AssertHasProperty(nestedSection, "division");
        AssertHasProperty(nestedSection, "level");
        AssertHasProperty(nestedSection, "shift");
        AssertHasProperty(nestedSection, "students");

        AssertCanonicalRoundTrip(sample, PlanCopeJsonSerializerContext.Default.GeRosterPackageDto);
    }

    [Fact]
    public void GeRosterSectionPackageDto_round_trips_through_source_generated_context()
    {
        var student = new GeRosterStudentPackageDto("stu-9001", "sec-501", 87654321, "40787654", "María", "Rodríguez");
        var sample = new GeRosterSectionPackageDto("sec-501", 1800554, "4° A", "División A", "Secundaria", "Mañana", new[] { student });

        var json = Serialize(sample, PlanCopeJsonSerializerContext.Default.GeRosterSectionPackageDto);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        AssertHasProperty(root, "id");
        AssertHasProperty(root, "geSectionId");
        AssertHasProperty(root, "course");
        AssertHasProperty(root, "division");
        AssertHasProperty(root, "level");
        AssertHasProperty(root, "shift");
        AssertHasProperty(root, "students");

        var nested = FirstElement(root.GetProperty("students"));
        AssertHasProperty(nested, "id");
        AssertHasProperty(nested, "sectionId");
        AssertHasProperty(nested, "gePersonId");
        AssertHasProperty(nested, "document");
        AssertHasProperty(nested, "firstName");
        AssertHasProperty(nested, "lastName");

        AssertCanonicalRoundTrip(sample, PlanCopeJsonSerializerContext.Default.GeRosterSectionPackageDto);
    }

    [Fact]
    public void GeRosterStudentPackageDto_round_trips_through_source_generated_context()
    {
        var sample = new GeRosterStudentPackageDto("stu-9001", "sec-501", 87654321, "40787654", "María", "Rodríguez");

        var json = Serialize(sample, PlanCopeJsonSerializerContext.Default.GeRosterStudentPackageDto);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        AssertHasProperty(root, "id");
        AssertHasProperty(root, "sectionId");
        AssertHasProperty(root, "gePersonId");
        AssertHasProperty(root, "document");
        AssertHasProperty(root, "firstName");
        AssertHasProperty(root, "lastName");

        Assert.Equal(sample, Deserialize(json, PlanCopeJsonSerializerContext.Default.GeRosterStudentPackageDto));
    }
}