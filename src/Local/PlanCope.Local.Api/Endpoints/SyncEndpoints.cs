using System.Text.Json;
using PlanCope.Local.Api.Data;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Local.Api.Services;
using PlanCope.Shared.Contracts.Sync;

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

        group.MapPost("/pull-exams", async (
            LocalExamPullService pullService,
            CancellationToken cancellationToken) =>
        {
            var result = await pullService.PullAsync(cancellationToken);
            return result.Success
                ? Results.Ok(result)
                : Results.BadRequest(result);
        });

        // Roster transport is deliberately manual. There is no hosted service,
        // timer, or implicit pull on startup; an operator/release invokes this
        // endpoint with the desired CUE and school year.
        group.MapPost("/pull-roster", async (
            GeRosterPullRequest? request,
            LocalRosterPullService pullService,
            CancellationToken cancellationToken) =>
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Cue) || string.IsNullOrWhiteSpace(request.SchoolYear))
            {
                return Results.BadRequest(new { error = "cue and schoolYear are required." });
            }

            var result = await pullService.PullAsync(request.Cue, request.SchoolYear, cancellationToken);
            return result.Success
                ? Results.Ok(result)
                : Results.BadRequest(result);
        });

        // Outbox transport is deliberately manual as well. A release/operator
        // invokes this endpoint; no background retry loop is registered.
        group.MapPost("/push-outbox", async (
            LocalOutboxPushRequest? request,
            LocalOutboxPushService pushService,
            CancellationToken cancellationToken) =>
        {
            var result = await pushService.PushAsync(request?.Limit ?? 50, cancellationToken);
            return result.Success && result.Pending == 0
                ? Results.Ok(result)
                : Results.Problem(result.Error ?? "Some outbox items remain pending.", statusCode: StatusCodes.Status502BadGateway, extensions: new Dictionary<string, object?>
                {
                    ["result"] = result
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

public sealed record LocalOutboxPushRequest(int Limit = 50);
