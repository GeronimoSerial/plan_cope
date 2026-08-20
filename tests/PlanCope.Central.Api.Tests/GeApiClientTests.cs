using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;
using PlanCope.Central.Api.Integrations.Ge;
using Xunit;

namespace PlanCope.Central.Api.Tests;

public sealed class GeApiClientTests
{
    [Fact]
    public async Task FindByDocument_ObtainsPasswordGrantAndSendsBearer_OnPageOne()
    {
        var handler = new RecordingHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/token")
            {
                return Json("{\"access_token\":\"token-1\",\"token_type\":\"bearer\",\"expires_in\":3600}");
            }

            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("token-1", request.Headers.Authorization?.Parameter);
            Assert.Contains("pageIndex=1", request.RequestUri?.Query, StringComparison.Ordinal);
            return Json("{\"data\":[{\"personaId\":42,\"apellido\":\"PEREZ\",\"nombre\":\"ANA\",\"nroDocumento\":\"12.345.678\"}],\"totalRegistros\":1}");
        });
        var options = CreateOptions(pageSize: 10);
        var tokenProvider = new GeTokenProvider(new HttpClient(handler) { BaseAddress = new Uri("https://ge.test/") }, options, new GeTokenCache());
        var api = new GeApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://ge.test/") }, tokenProvider, options);

        var student = await api.FindStudentByDocumentAsync("12-345-678");

        Assert.NotNull(student);
        Assert.Equal(42, student.PersonaId);
        Assert.Equal("PEREZ", student.Apellido);
        Assert.Equal("ANA", student.Nombre);
        Assert.Equal(1, handler.TokenRequests);
        var tokenBody = handler.TokenBodies.Single();
        Assert.Contains("username=ge-user%40example.com", tokenBody, StringComparison.Ordinal);
        Assert.Contains("password=secret", tokenBody, StringComparison.Ordinal);
        Assert.Contains("grant_type=password", tokenBody, StringComparison.Ordinal);
        Assert.DoesNotContain("client_id", tokenBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetStudentsBySchool_PaginatesFromOneAndDeduplicates()
    {
        var pageIndexes = new ConcurrentBag<string>();
        var handler = new RecordingHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/token")
            {
                return Json("{\"access_token\":\"token-1\",\"expires_in\":3600}");
            }

            pageIndexes.Add(GetQuery(request, "pageIndex"));
            return GetQuery(request, "pageIndex") switch
            {
                "1" => Json("{\"alumnos\":[{\"personaId\":1,\"establecimientoCursoDivisionId\":10,\"cueAnexo\":\"1800554-00\",\"persona\":{\"personaId\":1,\"apellido\":\"UNO\",\"nombre\":\"A\",\"nroDocumento\":\"111\"}},{\"personaId\":2,\"establecimientoCursoDivisionId\":20,\"cueAnexo\":\"1800554-00\",\"persona\":{\"personaId\":2,\"apellido\":\"DOS\",\"nombre\":\"B\",\"nroDocumento\":\"222\"}}],\"totalRegistros\":4}"),
                "2" => Json("{\"alumnos\":[{\"personaId\":2,\"establecimientoCursoDivisionId\":20,\"cueAnexo\":\"1800554-00\",\"persona\":{\"personaId\":2,\"apellido\":\"DOS\",\"nombre\":\"B\",\"nroDocumento\":\"222\"}},{\"personaId\":2,\"establecimientoCursoDivisionId\":30,\"cueAnexo\":\"1800554-00\",\"persona\":{\"personaId\":2,\"apellido\":\"DOS\",\"nombre\":\"B\",\"nroDocumento\":\"222\"}}],\"totalRegistros\":4}"),
                _ => throw new InvalidOperationException("Unexpected page."),
            };
        });
        var options = CreateOptions(pageSize: 2);
        var tokenProvider = new GeTokenProvider(new HttpClient(handler) { BaseAddress = new Uri("https://ge.test/") }, options, new GeTokenCache());
        var api = new GeApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://ge.test/") }, tokenProvider, options);

        var students = await api.GetStudentsBySchoolAsync("1800554-00", "2026");

        Assert.Equal(3, students.Count);
        Assert.Equal(["1", "2"], pageIndexes.OrderBy(value => value).ToArray());
        Assert.Equal([1, 2, 2], students.Select(student => student.PersonaId).OrderBy(id => id).ToArray());
        Assert.Equal([10, 20, 30], students.Select(student => student.EstablecimientoCursoDivisionId).OrderBy(id => id).ToArray());
    }

    [Fact]
    public async Task UnauthorizedResponse_InvalidatesTokenAndRetriesOnce()
    {
        var apiCalls = 0;
        var tokenCounter = 0;
        var handler = new RecordingHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/token")
            {
                var token = ++tokenCounter;
                return Json($"{{\"access_token\":\"token-{token}\",\"expires_in\":3600}}");
            }

            apiCalls++;
            if (request.Headers.Authorization?.Parameter == "token-1")
            {
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }

            return Json("[{\"personaId\":7,\"apellido\":\"OK\",\"nombre\":\"REINTENTO\",\"nroDocumento\":\"777\"}]");
        });
        var options = CreateOptions();
        var tokenProvider = new GeTokenProvider(new HttpClient(handler) { BaseAddress = new Uri("https://ge.test/") }, options, new GeTokenCache());
        var api = new GeApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://ge.test/") }, tokenProvider, options);

        var student = await api.FindStudentByDocumentAsync("777");

        Assert.NotNull(student);
        Assert.Equal(2, apiCalls);
        Assert.Equal(2, handler.TokenRequests);
    }

    [Fact]
    public async Task TokenProvider_CachesOneTokenAcrossConcurrentRequests()
    {
        var handler = new RecordingHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/token")
            {
                return Json("{\"access_token\":\"token-1\",\"expires_in\":3600}");
            }

            throw new InvalidOperationException("Token provider test should not call an API endpoint.");
        });
        var provider = new GeTokenProvider(new HttpClient(handler) { BaseAddress = new Uri("https://ge.test/") }, CreateOptions(), new GeTokenCache());

        var tokens = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => provider.GetAccessTokenAsync()));

        Assert.All(tokens, token => Assert.Equal("token-1", token));
        Assert.Equal(1, handler.TokenRequests);
    }

    [Fact]
    public async Task TwoProvidersWithSharedCache_ObtainOnlyOneToken()
    {
        var handler = new RecordingHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/token")
            {
                return Json("{\"access_token\":\"shared-token\",\"expires_in\":3600}");
            }

            throw new InvalidOperationException("Token provider test should not call an API endpoint.");
        });
        var cache = new GeTokenCache();
        var options = CreateOptions();
        var provider1 = new GeTokenProvider(new HttpClient(handler) { BaseAddress = new Uri("https://ge.test/") }, options, cache);
        var provider2 = new GeTokenProvider(new HttpClient(handler) { BaseAddress = new Uri("https://ge.test/") }, options, cache);

        var tokens = await Task.WhenAll(provider1.GetAccessTokenAsync(), provider2.GetAccessTokenAsync());

        Assert.Equal(["shared-token", "shared-token"], tokens);
        Assert.Equal(1, handler.TokenRequests);
    }

    private static IOptions<GeApiOptions> CreateOptions(int pageSize = 100)
    {
        return Options.Create(new GeApiOptions
        {
            BaseUrl = "https://ge.test/",
            Username = "ge-user@example.com",
            Password = "secret",
            PageSize = pageSize,
            MaxPages = 10,
            MaxStudents = 100,
            TimeoutSeconds = 10,
            TokenSafetyMarginSeconds = 60
        });
    }

    private static HttpResponseMessage Json(string content)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };
    }

    private static string GetQuery(HttpRequestMessage request, string name)
    {
        var query = request.RequestUri?.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries) ?? [];
        var pair = query.Select(value => value.Split('=', 2)).FirstOrDefault(value => value[0] == name);
        return pair is null ? string.Empty : Uri.UnescapeDataString(pair[1]);
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler = handler;

        public int TokenRequests { get; private set; }

        public List<string> TokenBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath == "/token")
            {
                TokenRequests++;
                TokenBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            }

            return _handler(request);
        }
    }
}
