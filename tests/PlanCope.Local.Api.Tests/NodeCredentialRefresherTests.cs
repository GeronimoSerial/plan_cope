using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Local.Api.Services;
using PlanCope.Shared.Contracts.Activation;
using PlanCope.Shared.Domain.Local;
using Xunit;

namespace PlanCope.Local.Api.Tests;

public sealed class NodeCredentialRefresherTests
{
    private const string CentralUrl = "https://central.test";

    [Fact]
    public async Task Missing_central_url_returns_false_without_http_call()
    {
        var syncState = new InMemorySyncStateRepository();
        var identityRepository = new InMemoryNodeIdentityRepository();
        var handler = new StubHttpMessageHandler();
        var refresher = CreateRefresher(syncState, identityRepository, handler);

        var result = await refresher.TryRefreshAsync(CancellationToken.None);

        Assert.False(result);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Missing_central_refresh_token_returns_false_without_http_call()
    {
        var syncState = new InMemorySyncStateRepository();
        await syncState.UpsertAsync(NewState("central_url", CentralUrl));
        var handler = new StubHttpMessageHandler();
        var refresher = CreateRefresher(syncState, new InMemoryNodeIdentityRepository(), handler);

        var result = await refresher.TryRefreshAsync(CancellationToken.None);

        Assert.False(result);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Successful_refresh_without_revocation_writes_all_four_sync_state_keys()
    {
        var syncState = new InMemorySyncStateRepository();
        await SeedRefreshableStateAsync(syncState);
        var accessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30);
        var refreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(30);
        var handler = new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new ActivationRefreshResponse
            {
                AccessToken = "new-access-token",
                RefreshToken = "new-refresh-token",
                AccessTokenExpiresAt = accessTokenExpiresAt,
                RefreshTokenExpiresAt = refreshTokenExpiresAt,
                NodeRevoked = false,
            })
        });
        var refresher = CreateRefresher(syncState, new InMemoryNodeIdentityRepository(), handler);

        var result = await refresher.TryRefreshAsync(CancellationToken.None);

        Assert.True(result);
        Assert.Equal("new-access-token", await ReadStateStringAsync(syncState, "central_access_token"));
        Assert.Equal("new-refresh-token", await ReadStateStringAsync(syncState, "central_refresh_token"));
        Assert.Equal(accessTokenExpiresAt.ToString("O"), await ReadStateStringAsync(syncState, "central_access_token_expires_at"));
        Assert.Equal(refreshTokenExpiresAt.ToString("O"), await ReadStateStringAsync(syncState, "central_refresh_token_expires_at"));
    }

    [Fact]
    public async Task NodeRevoked_response_marks_identity_revoked_and_returns_false()
    {
        var syncState = new InMemorySyncStateRepository();
        await SeedRefreshableStateAsync(syncState);
        var identityRepository = new InMemoryNodeIdentityRepository(NewIdentity("active"));
        var handler = new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new ActivationRefreshResponse
            {
                AccessToken = "new-access-token",
                RefreshToken = "new-refresh-token",
                AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30),
                RefreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
                NodeRevoked = true,
            })
        });
        var refresher = CreateRefresher(syncState, identityRepository, handler);

        var result = await refresher.TryRefreshAsync(CancellationToken.None);

        Assert.False(result);
        Assert.NotNull(identityRepository.Current);
        Assert.Equal("revoked", identityRepository.Current!.CredentialState);
    }

    [Fact]
    public async Task Non_success_status_marks_identity_revoked_and_returns_false()
    {
        var syncState = new InMemorySyncStateRepository();
        await SeedRefreshableStateAsync(syncState);
        var identityRepository = new InMemoryNodeIdentityRepository(NewIdentity("active"));
        var handler = new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var refresher = CreateRefresher(syncState, identityRepository, handler);

        var result = await refresher.TryRefreshAsync(CancellationToken.None);

        Assert.False(result);
        Assert.NotNull(identityRepository.Current);
        Assert.Equal("revoked", identityRepository.Current!.CredentialState);
    }

    private static async Task SeedRefreshableStateAsync(InMemorySyncStateRepository syncState)
    {
        await syncState.UpsertAsync(NewState("central_url", CentralUrl));
        await syncState.UpsertAsync(NewState("central_refresh_token", "existing-refresh-token"));
    }

    private static SyncState NewState(string key, string value) =>
        new(Guid.NewGuid().ToString("N"), key, JsonSerializer.Serialize(value), DateTimeOffset.UtcNow.ToString("O"));

    private static NodeIdentity NewIdentity(string credentialState) =>
        new("identity-1", null, "180055400", "fingerprint-hash", "{}", null, null, credentialState, null, null);

    private static async Task<string?> ReadStateStringAsync(InMemorySyncStateRepository syncState, string key)
    {
        var state = await syncState.GetAsync(key);
        return state is null ? null : JsonSerializer.Deserialize<string>(state.ValueJson);
    }

    private static NodeCredentialRefresher CreateRefresher(
        ISyncStateRepository syncState,
        INodeIdentityRepository nodeIdentity,
        HttpMessageHandler handler)
    {
        var factory = new StubHttpClientFactory(handler);
        return new NodeCredentialRefresher(factory, syncState, nodeIdentity);
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage? response;

        public StubHttpMessageHandler(HttpResponseMessage? response = null) => this.response = response;

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(response ?? new HttpResponseMessage(HttpStatusCode.OK));
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
        public InMemoryNodeIdentityRepository(NodeIdentity? initial = null) => Current = initial;

        public NodeIdentity? Current { get; private set; }

        public Task<NodeIdentity?> GetAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Current);

        public Task UpsertAsync(NodeIdentity identity, CancellationToken cancellationToken = default)
        {
            Current = identity;
            return Task.CompletedTask;
        }
    }
}