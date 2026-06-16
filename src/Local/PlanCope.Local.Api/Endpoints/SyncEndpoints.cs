using System.Text.Json;
using PlanCope.Local.Api.Data;
using PlanCope.Local.Api.Data.Repositories;

namespace PlanCope.Local.Api.Endpoints;

public static class SyncEndpoints
{
    public static IEndpointRouteBuilder MapSyncEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/sync");

        group.MapGet("/status", async (
            ISyncStateRepository syncStateRepository,
            IOutboxRepository outboxRepository,
            LocalDatabaseOptions databaseOptions,
            CancellationToken cancellationToken) =>
        {
            var nodeState = await syncStateRepository.GetAsync("node_id", cancellationToken);
            var lastPull = await syncStateRepository.GetAsync("last_pull_at", cancellationToken);
            var lastPush = await syncStateRepository.GetAsync("last_push_at", cancellationToken);
            var centralUrl = await syncStateRepository.GetAsync("central_url", cancellationToken);
            var pendingItems = await outboxRepository.CountPendingAsync(cancellationToken);

            return Results.Ok(new
            {
                nodeId = ReadJsonString(nodeState?.ValueJson),
                healthy = true,
                lastPullAt = ReadJsonString(lastPull?.ValueJson),
                lastPushAt = ReadJsonString(lastPush?.ValueJson),
                pendingItems,
                centralUrl = ReadJsonString(centralUrl?.ValueJson),
                database = databaseOptions.ConnectionString
            });
        });

        return endpoints;
    }

    private static string? ReadJsonString(string? valueJson)
    {
        if (string.IsNullOrWhiteSpace(valueJson))
        {
            return null;
        }

        using var document = JsonDocument.Parse(valueJson);
        return document.RootElement.ValueKind is JsonValueKind.String
            ? document.RootElement.GetString()
            : document.RootElement.GetRawText();
    }
}
