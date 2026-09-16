using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Local.Api.Services;
using PlanCope.Shared.Contracts.Activation;
using PlanCope.Shared.Domain.Local;
using Xunit;

namespace PlanCope.Local.Api.Tests;

public sealed class CentralCredentialHandlerTests
{
    private const string CentralUrl = "https://central.test";

    [Fact]
    public async Task Successful_first_call_attaches_bearer_token_and_does_not_retry()
    {
        var syncState = new InMemorySyncStateRepository();
        await syncState.UpsertAsync(NewState("central_access_token", "existing-access-token"));
        var inner = new SequenceHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var handler = BuildHandler(syncState, inner);
        var client = new HttpClient(handler);

        using var response = await client.GetAsync("https://central.test/api/nodes/self");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, inner.CallCount);
        Assert.Equal("Bearer existing-access-token", inner.Requests.Single().Headers.Authorization?.ToString());
    }

    [Fact]
    public async Task Unauthorized_then_refresh_then_retry_returns_success_after_two_inner_calls()
    {
        var syncState = new InMemorySyncStateRepository();
        await syncState.UpsertAsync(NewState("central_url", CentralUrl));
        await syncState.UpsertAsync(NewState("central_refresh_token", "existing-refresh-token"));
        var inner = new SequenceHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.Unauthorized),
            new HttpResponseMessage(HttpStatusCode.OK));
        var handler = BuildHandler(syncState, inner);
        var client = new HttpClient(handler);

        using var response = await client.GetAsync("https://central.test/api/nodes/self");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.CallCount);
        Assert.Equal("Bearer refreshed-access-token", inner.Requests[1].Headers.Authorization?.ToString());
    }

    [Fact]
    public async Task Unauthorized_without_refresh_returns_original_401_without_retry()
    {
        var syncState = new InMemorySyncStateRepository();
        await syncState.UpsertAsync(NewState("central_url", CentralUrl));
        var inner = new SequenceHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.Unauthorized),
            new HttpResponseMessage(HttpStatusCode.OK));
        var handler = BuildHandler(syncState, inner);
        var client = new HttpClient(handler);

        using var response = await client.GetAsync("https://central.test/api/nodes/self");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, inner.CallCount);
    }

    private static CentralCredentialHandler BuildHandler(ISyncStateRepository syncState, HttpMessageHandler inner)
    {
        var refresher = new NodeCredentialRefresher(
            new StubHttpClientFactory(new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new ActivationRefreshResponse
                {
                    AccessToken = "refreshed-access-token",
                    RefreshToken = "refreshed-refresh-token",
                    AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30),
                    RefreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
                    NodeRevoked = false,
                })
            })),
            syncState,
            new InMemoryNodeIdentityRepository());

        return new CentralCredentialHandler(syncState, refresher) { InnerHandler = inner };
    }

    private static SyncState NewState(string key, string value) =>
        new(Guid.NewGuid().ToString("N"), key, JsonSerializer.Serialize(value), DateTimeOffset.UtcNow.ToString("O"));

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage? response;

        public StubHttpMessageHandler(HttpResponseMessage? response = null) => this.response = response;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(response ?? new HttpResponseMessage(HttpStatusCode.OK));
    }

    private sealed class SequenceHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> responses;

        public SequenceHttpMessageHandler(params HttpResponseMessage[] responses)
            => this.responses = new Queue<HttpResponseMessage>(responses);

        public int CallCount { get; private set; }

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            Requests.Add(request);
            return Task.FromResult(responses.Count > 0 ? responses.Dequeue() : new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }

    private sealed class InMemorySyncStateRepository : ISyncStateRepository
    {
        private readonly Dictionary<string, SyncState> states = new(StringComparer.Ordinal);

        public Task<SyncState?> GetAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(states.TryGetValue(key, out var state) ? state : null);

        public Task UpsertAsync(SyncState state, CancellationToken cancellationToken = default)
        {
            states[state.Key] = state;
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryNodeIdentityRepository : INodeIdentityRepository
    {
        public Task<NodeIdentity?> GetAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<NodeIdentity?>(null);

        public Task UpsertAsync(NodeIdentity identity, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}