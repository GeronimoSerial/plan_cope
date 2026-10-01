using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlanCope.Central.Api.Controllers;
using PlanCope.Central.Api.Data;
using PlanCope.Central.Api.Services;
using PlanCope.Shared.Contracts.Exams;
using PlanCope.Shared.Contracts.Sync;
using PlanCope.Shared.Domain.Central;
using PlanCope.Shared.Infrastructure.Validation;
using Testcontainers.PostgreSql;
using Xunit;

namespace PlanCope.Central.Api.Tests;

public sealed class ExamAssetPostgresIntegrationTests
{
    [DockerFact]
    public async Task Postgres_upload_get_publish_and_sync_preserve_a_large_png_without_exposing_storage_data()
    {
        await using var postgres = new PostgreSqlBuilder().WithImage("postgres:17-alpine").Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<PlanCopeDbContext>()
            .UseNpgsql(postgres.GetConnectionString(), postgresOptions =>
                postgresOptions.MigrationsAssembly(typeof(PlanCope.Central.Migrations.Migrations.AddDeliverySessionNodeOwnership).Assembly.GetName().Name))
            .Options;

        await using var migrationContext = new PlanCopeDbContext(options);
        await migrationContext.Database.MigrateAsync();

        var imageBytes = CreatePng(720, 680);
        Assert.InRange(imageBytes.Length, 1_400_000, 1_700_000);
        await using var dbContext = new PlanCopeDbContext(options);
        var examsController = new ExamsController(
            dbContext,
            new ExamValidator(),
            new ExamVersionValidator(),
            new ExamBlockValidator())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var createExam = await examsController.Create(
            new CreateExamRequest("PG-ASSET-01", "Postgres Asset Test", null, ["primaria-6"], "Matemática", "Números"),
            CancellationToken.None);
        var exam = Assert.IsType<ExamSummaryDto>(Assert.IsType<CreatedAtActionResult>(createExam.Result).Value);
        var versionId = Assert.IsType<string>(exam.InitialVersionId);
        var upload = await examsController.CreateAsset(
            versionId,
            new CreateAssetRequest("pregunta.png", "image/png", Convert.ToBase64String(imageBytes)),
            CancellationToken.None);
        var assetDto = Assert.IsType<AssetDto>(Assert.IsType<CreatedAtActionResult>(upload.Result).Value);
        Assert.Equal("database", assetDto.StoragePath);
        Assert.DoesNotContain("base64:", assetDto.StoragePath, StringComparison.Ordinal);

        var assetResponse = await examsController.GetAssetContent(versionId, assetDto.Id, CancellationToken.None);
        var file = Assert.IsType<FileContentResult>(assetResponse);
        Assert.Equal(imageBytes, file.FileContents);
        Assert.Equal("image/png", file.ContentType);

        var versionResult = await examsController.GetVersion(versionId, CancellationToken.None);
        var versionDto = Assert.IsType<ExamVersionDto>(Assert.IsType<OkObjectResult>(versionResult.Result).Value);
        Assert.Equal("database", Assert.Single(versionDto.Assets).StoragePath);

        var upsert = await examsController.UpsertBlock(versionId, 0,
            new UpsertBlockRequest(0, PlanCope.Shared.Domain.BlockType.TrueFalse, "Pregunta", null,
                System.Text.Json.JsonDocument.Parse($"{{\"question\":\"Pregunta\",\"imageAssetId\":\"{assetDto.Id}\"}}").RootElement, null),
            CancellationToken.None);
        Assert.IsType<OkObjectResult>(upsert.Result);

        var publishResult = await examsController.PublishVersion(versionId,
            new PublishExamVersionRequest(null, null), CancellationToken.None);
        Assert.IsType<PublishExamVersionResponse>(Assert.IsType<OkObjectResult>(publishResult.Result).Value);

        var syncController = new SyncController(dbContext, new CentralStatsRollupService(dbContext));
        syncController.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim("token_type", "node_access"), new Claim("node_id", "asset-postgres-node")], "integration-test"))
            }
        };
        var pullResult = await syncController.Pull("asset-postgres-node", "0", 50, CancellationToken.None);
        var pull = Assert.IsType<PullResponse>(Assert.IsType<OkObjectResult>(pullResult.Result).Value);
        var payloadAsset = Assert.Single(Assert.Single(pull.Items).Payload.GetProperty("Assets").EnumerateArray());
        Assert.Equal(assetDto.Id, payloadAsset.GetProperty("Id").GetString());
        Assert.Equal(Convert.ToBase64String(imageBytes), payloadAsset.GetProperty("ContentBase64").GetString());
    }

    private static byte[] CreatePng(int width, int height)
    {
        var raw = new byte[height * (width * 3 + 1)];
        var random = new Random(271828);
        random.NextBytes(raw);
        for (var row = 0; row < height; row++) raw[row * (width * 3 + 1)] = 0;

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.NoCompression, leaveOpen: true))
            zlib.Write(raw);

        using var png = new MemoryStream();
        png.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
        header[8] = 8;
        header[9] = 2;
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);
        var crc = 0xffffffffu;
        foreach (var value in typeBytes.Concat(data.ToArray()))
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320u : crc >> 1;
        }
        Span<byte> checksum = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(checksum, ~crc);
        output.Write(checksum);
    }
}
