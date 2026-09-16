using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using PlanCope.Shared.Contracts.Exams;
using PlanCope.Shared.Contracts.Serialization;
using PlanCope.Shared.Domain;
using Xunit;

namespace PlanCope.SyncCompat.Tests;

public sealed class ExamsContractTests
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
    public void ExamSummaryDto_round_trips_through_source_generated_context()
    {
        var sample = new ExamSummaryDto(
            "ex-1",
            "EXA-2026-01",
            "Matemática · Primer Año",
            "Secundario",
            "Matemática",
            "Números y Operaciones",
            "Approved",
            3);

        var json = Serialize(sample, PlanCopeJsonSerializerContext.Default.ExamSummaryDto);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        AssertHasProperty(root, "id");
        AssertHasProperty(root, "code");
        AssertHasProperty(root, "title");
        AssertHasProperty(root, "level");
        AssertHasProperty(root, "area");
        AssertHasProperty(root, "subject");
        AssertHasProperty(root, "status");
        AssertHasProperty(root, "versionCount");

        AssertCanonicalRoundTrip(sample, PlanCopeJsonSerializerContext.Default.ExamSummaryDto);
    }

    [Fact]
    public void ExamVersionDto_round_trips_through_source_generated_context()
    {
        var sample = new ExamVersionDto(
            "ev-101",
            "ex-1",
            2,
            1,
            "Approved",
            JsonElementOf("""{"author":"teacher01","generatedBy":"AI"}"""),
            new[]
            {
                new BlockDto(
                    "blk-1",
                    "ev-101",
                    0,
                    BlockType.MultipleChoice,
                    "Pregunta 1",
                    "¿Cuánto es 2 + 2?",
                    JsonElementOf("""{"options":["A","B","C","D"],"answerIndex":1}"""),
                    JsonElementOf("""{"shuffled":true}""")),
                new BlockDto(
                    "blk-2",
                    "ev-101",
                    1,
                    BlockType.ShortAnswer,
                    "Pregunta 2",
                    "Escriba la capital de Francia",
                    JsonElementOf("""{"maxLength":120}"""),
                    null),
            },
            new[]
            {
                new AnswerKeyDto(
                    "ak-1",
                    "blk-1",
                    JsonElementOf("""{"option":"B"}"""),
                    1m,
                    JsonElementOf("""{"explanation":"2+2=4"}""")),
                new AnswerKeyDto(
                    "ak-2",
                    "blk-2",
                    JsonElementOf("""{"keywords":["París","Paris"]}"""),
                    2m,
                    null),
            },
            new[]
            {
                new AssetDto(
                    "as-1",
                    "ev-101",
                    "portada.png",
                    "image/png",
                    2048,
                    "sha256-aaa",
                    "exams/ev-101/portada.png"),
                new AssetDto(
                    "as-2",
                    "ev-101",
                    "audio.wav",
                    "audio/wav",
                    4096,
                    "sha256-bbb",
                    "exams/ev-101/audio.wav"),
            });

        var json = Serialize(sample, PlanCopeJsonSerializerContext.Default.ExamVersionDto);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        AssertHasProperty(root, "id");
        AssertHasProperty(root, "examId");
        AssertHasProperty(root, "versionNumber");
        AssertHasProperty(root, "schemaVersion");
        AssertHasProperty(root, "status");
        AssertHasProperty(root, "metadata");
        AssertHasProperty(root, "blocks");
        AssertHasProperty(root, "answerKeys");
        AssertHasProperty(root, "assets");

        AssertCanonicalRoundTrip(sample, PlanCopeJsonSerializerContext.Default.ExamVersionDto);
    }

    [Fact]
    public void BlockDto_round_trips_through_source_generated_context()
    {
        var sample = new BlockDto(
            "blk-9",
            "ev-55",
            3,
            BlockType.TrueFalse,
            "Enunciado",
            "La Tierra es plana",
            JsonElementOf("""{"correct":false}"""),
            JsonElementOf("""{"required":true}"""));

        var json = Serialize(sample, PlanCopeJsonSerializerContext.Default.BlockDto);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        AssertHasProperty(root, "id");
        AssertHasProperty(root, "versionId");
        AssertHasProperty(root, "orderIndex");
        AssertHasProperty(root, "blockType");
        AssertHasProperty(root, "title");
        AssertHasProperty(root, "description");
        AssertHasProperty(root, "config");
        AssertHasProperty(root, "validation");

        AssertCanonicalRoundTrip(sample, PlanCopeJsonSerializerContext.Default.BlockDto);
    }

    [Fact]
    public void AnswerKeyDto_round_trips_through_source_generated_context()
    {
        var sample = new AnswerKeyDto(
            "ak-77",
            "blk-12",
            JsonElementOf("""{"options":["A","B","C"],"answer":"C"}"""),
            1.5m,
            JsonElementOf("""{"feedback":"Bien resuelto"}"""));

        var json = Serialize(sample, PlanCopeJsonSerializerContext.Default.AnswerKeyDto);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        AssertHasProperty(root, "id");
        AssertHasProperty(root, "blockId");
        AssertHasProperty(root, "correctAnswer");
        AssertHasProperty(root, "scoreValue");
        AssertHasProperty(root, "metadata");

        AssertCanonicalRoundTrip(sample, PlanCopeJsonSerializerContext.Default.AnswerKeyDto);
    }

    [Fact]
    public void AssetDto_round_trips_through_source_generated_context()
    {
        var sample = new AssetDto(
            "as-4",
            "ev-101",
            "imagen-2.png",
            "image/png",
            8192,
            "sha256-ccc",
            "exams/ev-101/imagen-2.png");

        var json = Serialize(sample, PlanCopeJsonSerializerContext.Default.AssetDto);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        AssertHasProperty(root, "id");
        AssertHasProperty(root, "versionId");
        AssertHasProperty(root, "fileName");
        AssertHasProperty(root, "mimeType");
        AssertHasProperty(root, "sizeBytes");
        AssertHasProperty(root, "checksum");
        AssertHasProperty(root, "storagePath");

        AssertCanonicalRoundTrip(sample, PlanCopeJsonSerializerContext.Default.AssetDto);
    }

    [Fact]
    public void PublishedExamPackageDto_round_trips_through_source_generated_context()
    {
        var sample = new PublishedExamPackageDto(
            "pkg-500",
            "ex-1",
            "ev-101",
            "EXA-2026-01",
            "Matemática · Primer Año",
            2,
            1,
            "sha256-pkg-500",
            JsonElementOf("""{"publishedAt":"2026-09-10T08:00:00Z"}"""),
            new[]
            {
                new BlockDto(
                    "blk-1",
                    "ev-101",
                    0,
                    BlockType.Text,
                    "Consigna",
                    "Lea con atención",
                    JsonElementOf("""{"minWords":10}"""),
                    null),
            },
            new[]
            {
                new AnswerKeyDto(
                    "ak-1",
                    "blk-1",
                    JsonElementOf("""{"text":"respuesta libre"}"""),
                    3m,
                    null),
            },
            new[]
            {
                new PublishedAssetDto(
                    "as-1",
                    "ev-101",
                    "portada.png",
                    "image/png",
                    2048,
                    "sha256-aaa",
                    "iVBORw0KGgo="),
            },
            new[]
            {
                new PublicationTargetDto("School", "sch-7"),
                new PublicationTargetDto("Global", null),
            });

        var json = Serialize(sample, PlanCopeJsonSerializerContext.Default.PublishedExamPackageDto);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        AssertHasProperty(root, "packageId");
        AssertHasProperty(root, "examId");
        AssertHasProperty(root, "examVersionId");
        AssertHasProperty(root, "examCode");
        AssertHasProperty(root, "title");
        AssertHasProperty(root, "versionNumber");
        AssertHasProperty(root, "schemaVersion");
        AssertHasProperty(root, "checksum");
        AssertHasProperty(root, "metadata");
        AssertHasProperty(root, "blocks");
        AssertHasProperty(root, "answerKeys");
        AssertHasProperty(root, "assets");
        AssertHasProperty(root, "targets");

        AssertCanonicalRoundTrip(sample, PlanCopeJsonSerializerContext.Default.PublishedExamPackageDto);
    }

    [Fact]
    public void PublishedAssetDto_round_trips_through_source_generated_context()
    {
        var sample = new PublishedAssetDto(
            "as-9",
            "ev-101",
            "guia.pdf",
            "application/pdf",
            16384,
            "sha256-ddd",
            "JVBERi0xLjQK");

        var json = Serialize(sample, PlanCopeJsonSerializerContext.Default.PublishedAssetDto);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        AssertHasProperty(root, "id");
        AssertHasProperty(root, "versionId");
        AssertHasProperty(root, "fileName");
        AssertHasProperty(root, "mimeType");
        AssertHasProperty(root, "sizeBytes");
        AssertHasProperty(root, "checksum");
        AssertHasProperty(root, "contentBase64");

        AssertCanonicalRoundTrip(sample, PlanCopeJsonSerializerContext.Default.PublishedAssetDto);
    }

    [Fact]
    public void PublicationTargetDto_round_trips_through_source_generated_context()
    {
        var sample = new PublicationTargetDto("Department", "dep-3");

        var json = Serialize(sample, PlanCopeJsonSerializerContext.Default.PublicationTargetDto);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        AssertHasProperty(root, "targetType");
        AssertHasProperty(root, "targetId");

        AssertCanonicalRoundTrip(sample, PlanCopeJsonSerializerContext.Default.PublicationTargetDto);
    }

    [Fact]
    public void CreateExamRequest_round_trips_through_source_generated_context()
    {
        var sample = new CreateExamRequest(
            "EXA-2026-02",
            "Lengua · Segundo Año",
            "Evaluación de comprensión lectora",
            "Secundario",
            "Lengua",
            "Literatura");

        var json = Serialize(sample, PlanCopeJsonSerializerContext.Default.CreateExamRequest);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        AssertHasProperty(root, "code");
        AssertHasProperty(root, "title");
        AssertHasProperty(root, "description");
        AssertHasProperty(root, "level");
        AssertHasProperty(root, "area");
        AssertHasProperty(root, "subject");

        AssertCanonicalRoundTrip(sample, PlanCopeJsonSerializerContext.Default.CreateExamRequest);
    }

    [Fact]
    public void CreateExamVersionRequest_round_trips_through_source_generated_context()
    {
        var sample = new CreateExamVersionRequest(
            1,
            JsonElementOf("""{"generatedBy":"teacher01","notes":"borrador"}"""));

        var json = Serialize(sample, PlanCopeJsonSerializerContext.Default.CreateExamVersionRequest);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        AssertHasProperty(root, "schemaVersion");
        AssertHasProperty(root, "metadata");

        AssertCanonicalRoundTrip(sample, PlanCopeJsonSerializerContext.Default.CreateExamVersionRequest);
    }

    [Fact]
    public void UpsertBlockRequest_round_trips_through_source_generated_context()
    {
        var sample = new UpsertBlockRequest(
            4,
            BlockType.ShortAnswer,
            "Pregunta 5",
            "Desarrolle la respuesta",
            JsonElementOf("""{"maxLength":200}"""),
            JsonElementOf("""{"allowBlank":false}"""));

        var json = Serialize(sample, PlanCopeJsonSerializerContext.Default.UpsertBlockRequest);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        AssertHasProperty(root, "orderIndex");
        AssertHasProperty(root, "blockType");
        AssertHasProperty(root, "title");
        AssertHasProperty(root, "description");
        AssertHasProperty(root, "config");
        AssertHasProperty(root, "validation");

        AssertCanonicalRoundTrip(sample, PlanCopeJsonSerializerContext.Default.UpsertBlockRequest);
    }

    [Fact]
    public void CreateAssetRequest_round_trips_through_source_generated_context()
    {
        var sample = new CreateAssetRequest("audio.mp3", "audio/mpeg", "SUQzBAAAAA");

        var json = Serialize(sample, PlanCopeJsonSerializerContext.Default.CreateAssetRequest);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        AssertHasProperty(root, "fileName");
        AssertHasProperty(root, "mimeType");
        AssertHasProperty(root, "contentBase64");

        AssertCanonicalRoundTrip(sample, PlanCopeJsonSerializerContext.Default.CreateAssetRequest);
    }

    [Fact]
    public void PublishExamVersionRequest_round_trips_through_source_generated_context()
    {
        var sample = new PublishExamVersionRequest("Matemática", "1°", "A");

        var json = Serialize(sample, PlanCopeJsonSerializerContext.Default.PublishExamVersionRequest);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        AssertHasProperty(root, "subject");
        AssertHasProperty(root, "grade");
        AssertHasProperty(root, "division");

        AssertCanonicalRoundTrip(sample, PlanCopeJsonSerializerContext.Default.PublishExamVersionRequest);
    }

    [Fact]
    public void PublishExamVersionResponse_round_trips_through_source_generated_context()
    {
        var sample = new PublishExamVersionResponse(
            "pkg-501",
            "ev-101",
            3,
            "sha256-pkg-501",
            new[]
            {
                new PublicationTargetDto("School", "sch-7"),
                new PublicationTargetDto("Global", null),
            });

        var json = Serialize(sample, PlanCopeJsonSerializerContext.Default.PublishExamVersionResponse);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        AssertHasProperty(root, "packageId");
        AssertHasProperty(root, "examVersionId");
        AssertHasProperty(root, "packageVersion");
        AssertHasProperty(root, "checksum");
        AssertHasProperty(root, "targets");

        AssertCanonicalRoundTrip(sample, PlanCopeJsonSerializerContext.Default.PublishExamVersionResponse);
    }

    [Fact]
    public void ReplaceExamDocumentRequest_round_trips_through_source_generated_context()
    {
        var sample = new ReplaceExamDocumentRequest(
            JsonElementOf("""{"revision":7,"editedBy":"teacher01"}"""),
            new[]
            {
                new DocumentBlockDto(
                    0,
                    BlockType.MultipleChoice,
                    "Pregunta 1",
                    "¿Cuánto es 2 + 2?",
                    JsonElementOf("""{"options":["A","B","C","D"]}"""),
                    JsonElementOf("""{"required":true}"""),
                    JsonElementOf("""{"option":"B"}"""),
                    1m),
                new DocumentBlockDto(
                    1,
                    BlockType.Image,
                    "Imagen 1",
                    null,
                    JsonElementOf("""{"assetId":"as-1","width":640}"""),
                    null,
                    null,
                    null),
            });

        var json = Serialize(sample, PlanCopeJsonSerializerContext.Default.ReplaceExamDocumentRequest);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        AssertHasProperty(root, "metadata");
        AssertHasProperty(root, "blocks");

        AssertCanonicalRoundTrip(sample, PlanCopeJsonSerializerContext.Default.ReplaceExamDocumentRequest);
    }

    [Fact]
    public void DocumentBlockDto_round_trips_through_source_generated_context()
    {
        var sample = new DocumentBlockDto(
            2,
            BlockType.TrueFalse,
            "Enunciado 2",
            "La capital de Francia es París",
            JsonElementOf("""{"correct":true}"""),
            JsonElementOf("""{"required":false}"""),
            JsonElementOf("""{"boolean":true}"""),
            0.5m);

        var json = Serialize(sample, PlanCopeJsonSerializerContext.Default.DocumentBlockDto);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        AssertHasProperty(root, "orderIndex");
        AssertHasProperty(root, "blockType");
        AssertHasProperty(root, "title");
        AssertHasProperty(root, "description");
        AssertHasProperty(root, "config");
        AssertHasProperty(root, "validation");
        AssertHasProperty(root, "correctAnswer");
        AssertHasProperty(root, "scoreValue");

        AssertCanonicalRoundTrip(sample, PlanCopeJsonSerializerContext.Default.DocumentBlockDto);
    }
}