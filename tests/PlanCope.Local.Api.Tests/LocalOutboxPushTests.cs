using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using PlanCope.Local.Api.Data;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Local.Api.Services;
using PlanCope.Shared.Contracts.Sync;
using PlanCope.Shared.Domain.Local;
using Xunit;

namespace PlanCope.Local.Api.Tests;

public sealed class LocalOutboxPushTests
{
    [Fact]
    public async Task Manual_push_sends_nominal_identity_without_document_or_token_and_marks_accepted()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"plancope-outbox-{Guid.NewGuid():N}.db");
        try
        {
            Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
            var connectionString = $"Data Source={databasePath};Pooling=False";
            new LocalDatabaseInitializer(new LocalDatabaseOptions(connectionString)).Initialize();
            var factory = new LocalSqliteConnectionFactory(new LocalDatabaseOptions(connectionString));
            var state = new SyncStateRepository(factory);
            var outbox = new OutboxRepository(factory);
            await state.UpsertAsync(new SyncState("node", "central_url", JsonSerializer.Serialize("https://central.test"), DateTimeOffset.UtcNow.ToString("O")));
            await state.UpsertAsync(new SyncState("node-id", "node_id", JsonSerializer.Serialize("node-1"), DateTimeOffset.UtcNow.ToString("O")));
            var payload = JsonSerializer.Serialize(new
            {
                attempt = new
                {
                    id = "attempt-1",
                    deliverySessionId = "session-1",
                    studentCode = "GE:42",
                    status = "submitted",
                    startedAt = DateTimeOffset.UtcNow.ToString("O"),
                    submittedAt = DateTimeOffset.UtcNow.ToString("O"),
                    localSequence = 1,
                    gePersonId = 42,
                    rosterStudentId = "roster-1",
                    studentFirstName = "Ana",
                    studentLastName = "Pérez",
                    documentLast4 = "5678",
                    verificationSource = "ge_roster",
                    verifiedAt = DateTimeOffset.UtcNow.ToString("O")
                },
                answers = Array.Empty<object>(),
                rosterSnapshotId = "snapshot-1",
                rosterSectionId = "section-1"
            });
            await outbox.InsertAsync(new SyncOutbox("outbox-1", SyncEventTypes.AttemptSubmitted, "student_attempt", "attempt-1", "idempotency-1", payload, "pending", 0, null, null, DateTimeOffset.UtcNow.ToString("O"), null));

            string? sentJson = null;
            var handler = new DelegateHandler((request, cancellationToken) =>
            {
                sentJson = request.Content!.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new PushResponse(1, 0, [new PushItemResult("idempotency-1", "accepted", null)]))
                });
            });
            var service = new LocalOutboxPushService(new TestHttpClientFactory(handler), state, outbox);

            var result = await service.PushAsync(20);

            Assert.True(result.Success);
            Assert.Equal(1, result.Accepted);
            Assert.DoesNotContain("12345678", sentJson, StringComparison.Ordinal);
            Assert.DoesNotContain("documentHash", sentJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("resolutionToken", sentJson, StringComparison.OrdinalIgnoreCase);
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            Assert.Equal("sent", connection.ExecuteScalar<string>("SELECT status FROM sync_outbox WHERE id = 'outbox-1';"));
        }
        finally
        {
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Manual_push_failure_keeps_item_pending_for_retry()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"plancope-outbox-{Guid.NewGuid():N}.db");
        try
        {
            Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
            var connectionString = $"Data Source={databasePath};Pooling=False";
            new LocalDatabaseInitializer(new LocalDatabaseOptions(connectionString)).Initialize();
            var factory = new LocalSqliteConnectionFactory(new LocalDatabaseOptions(connectionString));
            var state = new SyncStateRepository(factory);
            var outbox = new OutboxRepository(factory);
            await state.UpsertAsync(new SyncState("node", "central_url", JsonSerializer.Serialize("https://central.test"), DateTimeOffset.UtcNow.ToString("O")));
            await state.UpsertAsync(new SyncState("node-id", "node_id", JsonSerializer.Serialize("node-1"), DateTimeOffset.UtcNow.ToString("O")));
            await outbox.InsertAsync(new SyncOutbox("outbox-1", SyncEventTypes.AttemptSubmitted, "student_attempt", "attempt-1", "idempotency-1", "{\"attempt\":{\"id\":\"attempt-1\",\"studentCode\":\"AUTO-0001\"}}", "pending", 0, null, null, DateTimeOffset.UtcNow.ToString("O"), null));
            var service = new LocalOutboxPushService(new TestHttpClientFactory(new DelegateHandler((_, _) => throw new HttpRequestException("offline"))), state, outbox);

            var result = await service.PushAsync(20);

            Assert.False(result.Success);
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            Assert.Equal("pending", connection.ExecuteScalar<string>("SELECT status FROM sync_outbox WHERE id = 'outbox-1';"));
            Assert.Equal(1L, connection.ExecuteScalar<long>("SELECT retry_count FROM sync_outbox WHERE id = 'outbox-1';"));
        }
        finally
        {
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Manual_push_with_invalid_and_accepted_items_reports_partial_failure_and_requeues_invalid_once()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"plancope-outbox-{Guid.NewGuid():N}.db");
        try
        {
            Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
            var connectionString = $"Data Source={databasePath};Pooling=False";
            new LocalDatabaseInitializer(new LocalDatabaseOptions(connectionString)).Initialize();
            var factory = new LocalSqliteConnectionFactory(new LocalDatabaseOptions(connectionString));
            var state = new SyncStateRepository(factory);
            var outbox = new OutboxRepository(factory);
            await state.UpsertAsync(new SyncState("node", "central_url", JsonSerializer.Serialize("https://central.test"), DateTimeOffset.UtcNow.ToString("O")));
            await state.UpsertAsync(new SyncState("node-id", "node_id", JsonSerializer.Serialize("node-1"), DateTimeOffset.UtcNow.ToString("O")));
            await outbox.InsertAsync(new SyncOutbox("invalid", SyncEventTypes.AttemptSubmitted, "student_attempt", "invalid", "invalid-key", "{bad-json", "pending", 0, null, null, DateTimeOffset.UtcNow.ToString("O"), null));
            var validPayload = "{\"attempt\":{\"id\":\"valid\",\"studentCode\":\"AUTO-0001\"}}";
            await outbox.InsertAsync(new SyncOutbox("valid", SyncEventTypes.AttemptSubmitted, "student_attempt", "valid", "valid-key", validPayload, "pending", 0, null, null, DateTimeOffset.UtcNow.ToString("O"), null));
            var handler = new DelegateHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new PushResponse(1, 0, [new PushItemResult("valid-key", "accepted", null)]))
            }));
            var service = new LocalOutboxPushService(new TestHttpClientFactory(handler), state, outbox);

            var result = await service.PushAsync(20);

            Assert.False(result.Success);
            Assert.Equal(1, result.Accepted);
            Assert.Equal(1, result.Pending);
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            Assert.Equal("pending", connection.ExecuteScalar<string>("SELECT status FROM sync_outbox WHERE id = 'invalid';"));
            Assert.Equal(1L, connection.ExecuteScalar<long>("SELECT retry_count FROM sync_outbox WHERE id = 'invalid';"));
            Assert.Equal("sent", connection.ExecuteScalar<string>("SELECT status FROM sync_outbox WHERE id = 'valid';"));
        }
        finally
        {
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }

    private sealed class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }

    private sealed class DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => callback(request, cancellationToken);
    }
}
