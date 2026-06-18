using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using PlanCope.Shared.Contracts.Local;
using PlanCope.Shared.Domain.Local;
using Xunit;

namespace PlanCope.Local.Api.Tests;

public sealed class LocalExamImportTests
{
    [Fact]
    public async Task Importing_exam_with_image_creates_local_exam_and_serves_asset()
    {
        using var factory = new LocalImportApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);

        var importResponse = await client.PostAsJsonAsync("/api/exams/import", ValidImportRequest());
        Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);

        using var importBody = JsonDocument.Parse(await importResponse.Content.ReadAsStringAsync());
        var examId = importBody.RootElement.GetProperty("examId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(examId));

        var exams = await client.GetFromJsonAsync<IReadOnlyList<LocalExamVersion>>("/api/exams/");
        Assert.NotNull(exams);
        Assert.Contains(exams, exam => exam.Id == examId && exam.ExamCode == "MAT-LOCAL-1");

        var blocks = await client.GetFromJsonAsync<IReadOnlyList<LocalExamBlock>>($"/api/exams/{examId}/blocks");
        Assert.NotNull(blocks);
        Assert.Equal(4, blocks.Count);
        Assert.Contains(blocks, block => block.BlockType == Shared.Domain.BlockType.Image);

        var assetResponse = await client.GetAsync("/api/assets/figure-1");
        Assert.Equal(HttpStatusCode.OK, assetResponse.StatusCode);
        Assert.Equal("image/svg+xml", assetResponse.Content.Headers.ContentType?.MediaType);

        var sessionResponse = await client.PostAsJsonAsync("/api/sessions/", new CreateSessionRequest(
            examId!,
            "CUE-DEMO",
            "6A",
            null,
            "Operador",
            30,
            null));
        Assert.Equal(HttpStatusCode.Created, sessionResponse.StatusCode);

        var session = await sessionResponse.Content.ReadFromJsonAsync<LocalDeliverySession>();
        Assert.NotNull(session);

        var attemptResponse = await client.PostAsync($"/api/sessions/{session.AccessCode}/attempts", null);
        Assert.Equal(HttpStatusCode.Created, attemptResponse.StatusCode);
    }

    [Fact]
    public async Task Extensive_sample_exam_imports_successfully()
    {
        using var factory = new LocalImportApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);

        await using var stream = File.OpenRead(FindRepositoryFile("docs", "local-exam-extensive-sample.json"));
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        var response = await client.PostAsync("/api/exams/import", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var importBody = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var examId = importBody.RootElement.GetProperty("examId").GetString();
        Assert.Equal("demo-integrado-6-v1", examId);
        Assert.Equal(16, importBody.RootElement.GetProperty("blockCount").GetInt32());
        Assert.Equal(3, importBody.RootElement.GetProperty("assetCount").GetInt32());

        var blocks = await client.GetFromJsonAsync<IReadOnlyList<LocalExamBlock>>($"/api/exams/{examId}/blocks");
        Assert.NotNull(blocks);
        Assert.Equal(16, blocks.Count);
        Assert.Equal(3, blocks.Count(block => block.BlockType == Shared.Domain.BlockType.Image));

        var assetResponse = await client.GetAsync("/api/assets/grafico-botellas");
        Assert.Equal(HttpStatusCode.OK, assetResponse.StatusCode);
    }

    [Fact]
    public async Task Reimporting_same_exam_updates_blocks_without_duplicates()
    {
        using var factory = new LocalImportApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);

        var first = await client.PostAsJsonAsync("/api/exams/import", ValidImportRequest());
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var secondRequest = ValidImportRequest(new object[]
        {
            new
            {
                id = "intro",
                type = "text",
                config = new { content = "Consigna actualizada." },
                validation = (object?)null
            },
            new
            {
                id = "q1",
                type = "multiple_choice",
                config = new
                {
                    question = "Cuanto es 10 + 5?",
                    options = new[] { new { value = "15", label = "15" }, new { value = "20", label = "20" } }
                },
                validation = new { required = true }
            }
        });

        var second = await client.PostAsJsonAsync("/api/exams/import", secondRequest);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        using var secondBody = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        var examId = secondBody.RootElement.GetProperty("examId").GetString();
        var blocks = await client.GetFromJsonAsync<IReadOnlyList<LocalExamBlock>>($"/api/exams/{examId}/blocks");

        Assert.NotNull(blocks);
        Assert.Equal(2, blocks.Count);
        Assert.Contains("actualizada", blocks[0].ConfigJson, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(InvalidImports))]
    public async Task Invalid_imports_return_bad_request(object request)
    {
        using var factory = new LocalImportApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);

        var response = await client.PostAsJsonAsync("/api/exams/import", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("errors", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Student_exam_route_serves_react_app()
    {
        using var factory = new LocalImportApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);

        var response = await client.GetAsync("/examen/ABCDE");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("root", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Asset_endpoint_rejects_unregistered_and_out_of_root_paths()
    {
        using var factory = new LocalImportApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);

        var traversalResponse = await client.GetAsync("/api/assets/%2e%2e%5csecret");
        Assert.Equal(HttpStatusCode.NotFound, traversalResponse.StatusCode);

        using var connection = factory.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO local_exam_versions (id, remote_exam_version_id, exam_code, version_number, checksum, metadata_json, schema_version, synced_at)
            VALUES ('asset-test', 'manual:asset-test', 'ASSET-TEST', 1, 'checksum', '{}', 1, $now);

            INSERT INTO local_assets (id, remote_asset_id, local_exam_version_id, file_name, mime_type, checksum, local_path, synced_at)
            VALUES ('outside', 'manual:outside', 'asset-test', 'outside.svg', 'image/svg+xml', 'checksum', $path, $now);
            """;
        command.Parameters.AddWithValue("$path", Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}.svg"));
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();

        var outsideResponse = await client.GetAsync("/api/assets/outside");
        Assert.Equal(HttpStatusCode.NotFound, outsideResponse.StatusCode);
    }

    public static IEnumerable<object[]> InvalidImports()
    {
        yield return new object[]
        {
            ValidImportRequest(new[]
            {
                new
                {
                    id = "bad-multiple",
                    type = "multiple_choice",
                    config = new
                    {
                        question = "Una sola opcion",
                        options = new[] { new { value = "a", label = "A" } }
                    },
                    validation = new { required = true }
                }
            })
        };

        yield return new object[]
        {
            ValidImportRequest(new[]
            {
                new
                {
                    id = "bad-image",
                    type = "image",
                    config = new { assetId = "missing-asset" },
                    validation = (object?)null
                }
            })
        };

        yield return new object[]
        {
            ValidImportRequest(new[]
            {
                new
                {
                    id = "bad-validation",
                    type = "short_answer",
                    config = new { prompt = "Explica." },
                    validation = true
                }
            })
        };

        yield return new object[]
        {
            ValidImportRequest(new[]
            {
                new
                {
                    id = "bad-required",
                    type = "short_answer",
                    config = new { prompt = "Explica." },
                    validation = new { required = "yes" }
                }
            })
        };

        yield return new object[]
        {
            new
            {
                examCode = "BAD-ASSET-PATH",
                title = "Asset path no permitido",
                versionNumber = 1,
                assets = new[]
                {
                    new
                    {
                        id = "local-file",
                        fileName = "local.svg",
                        mimeType = "image/svg+xml",
                        sourcePath = Path.Combine(Path.GetTempPath(), "local.svg")
                    }
                },
                blocks = new object[]
                {
                    new
                    {
                        id = "image",
                        type = "image",
                        config = new { assetId = "local-file" },
                        validation = (object?)null
                    }
                }
            }
        };
    }

    private static object ValidImportRequest(IEnumerable<object>? blocks = null)
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="120" height="80">
              <rect width="120" height="80" fill="#d9f0ee"/>
            </svg>
            """;

        return new
        {
            id = "mat-local-1-v1",
            examCode = "MAT-LOCAL-1",
            title = "Matematica local",
            grade = "6",
            division = "A",
            subject = "Matematica",
            versionNumber = 1,
            assets = new[]
            {
                new
                {
                    id = "figure-1",
                    fileName = "figure.svg",
                    mimeType = "image/svg+xml",
                    contentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(svg))
                }
            },
            blocks = blocks ?? new object[]
            {
                new
                {
                    id = "intro",
                    type = "text",
                    config = new { content = "Lee todo antes de responder." },
                    validation = (object?)null
                },
                new
                {
                    id = "figure",
                    type = "image",
                    config = new { assetId = "figure-1", alt = "Figura", caption = "Observa la figura." },
                    validation = (object?)null
                },
                new
                {
                    id = "q1",
                    type = "multiple_choice",
                    config = new
                    {
                        question = "Cuanto es 18 + 24?",
                        options = new[] { new { value = "42", label = "42" }, new { value = "44", label = "44" } }
                    },
                    validation = new { required = true }
                },
                new
                {
                    id = "q2",
                    type = "short_answer",
                    config = new { prompt = "Explica como lo pensaste." },
                    validation = new { required = true }
                }
            }
        };
    }

    private static async Task EnsureInitializedAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(new[] { current.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file {Path.Combine(segments)}.");
    }

    private sealed class LocalImportApiFactory : WebApplicationFactory<Program>
    {
        private readonly string assetsPath = Path.Combine(Path.GetTempPath(), $"plancope-assets-{Guid.NewGuid():N}");
        private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"plancope-local-{Guid.NewGuid():N}.db");
        private readonly string clientDistPath = Path.Combine(Path.GetTempPath(), $"plancope-client-{Guid.NewGuid():N}");
        private readonly string? previousAssetsPath;
        private readonly string? previousClientDistPath;
        private readonly string? previousConnectionString;
        private readonly string? previousSeedDemoExam;
        private string ConnectionString => $"Data Source={databasePath};Pooling=False";

        public LocalImportApiFactory()
        {
            previousAssetsPath = Environment.GetEnvironmentVariable("Local__AssetsPath");
            previousClientDistPath = Environment.GetEnvironmentVariable("PLANCOPE_CLIENT_APP_DIST");
            previousConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__LocalDatabase");
            previousSeedDemoExam = Environment.GetEnvironmentVariable("Local__SeedDemoExam");
            Directory.CreateDirectory(clientDistPath);
            File.WriteAllText(Path.Combine(clientDistPath, "index.html"), """<html><body><div id="root"></div></body></html>""");
            Environment.SetEnvironmentVariable("Local__AssetsPath", assetsPath);
            Environment.SetEnvironmentVariable("PLANCOPE_CLIENT_APP_DIST", clientDistPath);
            Environment.SetEnvironmentVariable("ConnectionStrings__LocalDatabase", ConnectionString);
            Environment.SetEnvironmentVariable("Local__SeedDemoExam", "false");
        }

        public SqliteConnection CreateConnection()
        {
            var connection = new SqliteConnection(ConnectionString);
            connection.Open();
            return connection;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration(configuration =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:LocalDatabase"] = ConnectionString,
                    ["Local:AssetsPath"] = assetsPath,
                    ["Local:SeedDemoExam"] = "false"
                });
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            Environment.SetEnvironmentVariable("Local__AssetsPath", previousAssetsPath);
            Environment.SetEnvironmentVariable("PLANCOPE_CLIENT_APP_DIST", previousClientDistPath);
            Environment.SetEnvironmentVariable("ConnectionStrings__LocalDatabase", previousConnectionString);
            Environment.SetEnvironmentVariable("Local__SeedDemoExam", previousSeedDemoExam);
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }

            if (Directory.Exists(assetsPath))
            {
                Directory.Delete(assetsPath, recursive: true);
            }

            if (Directory.Exists(clientDistPath))
            {
                Directory.Delete(clientDistPath, recursive: true);
            }
        }
    }
}
