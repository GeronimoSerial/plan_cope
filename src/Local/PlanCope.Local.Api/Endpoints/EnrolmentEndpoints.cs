using System.Net.Http.Json;
using System.Text.Json;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Local.Api.Services;
using PlanCope.Shared.Contracts.Activation;
using PlanCope.Shared.Domain.Local;

namespace PlanCope.Local.Api.Endpoints;

public static class EnrolmentEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapEnrolmentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/enrolment");

        group.MapPost("/redeem", async (
            EnrolmentRedeemRequest request,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            ISyncStateRepository syncStateRepository,
            INodeIdentityRepository nodeIdentityRepository,
            HardwareFingerprintService fingerprintService,
            CancellationToken ct) =>
        {
            var identity = await nodeIdentityRepository.GetAsync(ct);
            if (identity is null)
            {
                return Results.BadRequest(new { error = "Activá el equipo antes de inscribirlo." });
            }

            var centralUrl = await ReadStateStringAsync(syncStateRepository, "central_url", ct)
                ?? configuration["Central:BaseUrl"];
            if (string.IsNullOrWhiteSpace(centralUrl))
            {
                return Results.BadRequest(new { error = "No hay una URL de Central configurada." });
            }
            await UpsertStateStringAsync(syncStateRepository, "central_url", centralUrl, ct);

            var fingerprint = fingerprintService.ComputeFingerprint();
            var client = httpClientFactory.CreateClient(nameof(EnrolmentEndpoints));
            client.BaseAddress = new Uri(centralUrl.Trim().TrimEnd('/') + "/");
            var redeemRequest = new ActivationRedeemRequest(
                request.ActivationKey,
                fingerprint.CompositeHash,
                JsonDocument.Parse(fingerprint.ComponentsJson),
                identity.Cue,
                AppVersion: null);

            var response = await client.PostAsJsonAsync("api/activation/redeem", redeemRequest, ct);
            if (!response.IsSuccessStatusCode)
            {
                return Results.BadRequest(new { error = "No se pudo validar la clave de activación. Verificá que sea correcta y no esté vencida o revocada." });
            }

            var redeemed = await response.Content.ReadFromJsonAsync<ActivationRedeemResponse>(cancellationToken: ct);
            if (redeemed is null)
            {
                return Results.BadRequest(new { error = "Central devolvió una respuesta inválida." });
            }

            await UpsertStateStringAsync(syncStateRepository, "node_id", redeemed.NodeId, ct);
            await UpsertStateStringAsync(syncStateRepository, "central_access_token", redeemed.AccessToken, ct);
            await UpsertStateStringAsync(syncStateRepository, "central_refresh_token", redeemed.RefreshToken, ct);
            await UpsertStateStringAsync(syncStateRepository, "central_access_token_expires_at", redeemed.AccessTokenExpiresAt.ToString("O"), ct);
            await UpsertStateStringAsync(syncStateRepository, "central_refresh_token_expires_at", redeemed.RefreshTokenExpiresAt.ToString("O"), ct);

            await nodeIdentityRepository.UpsertAsync(identity with
            {
                NodeId = redeemed.NodeId,
                EnrolledAt = DateTimeOffset.UtcNow.ToString("O"),
                CredentialState = "active"
            }, ct);

            return Results.Ok(new { nodeId = redeemed.NodeId });
        });

        return endpoints;
    }

    private static async Task<string?> ReadStateStringAsync(ISyncStateRepository repository, string key, CancellationToken ct)
    {
        var state = await repository.GetAsync(key, ct);
        if (string.IsNullOrWhiteSpace(state?.ValueJson)) return null;
        using var document = JsonDocument.Parse(state.ValueJson);
        return document.RootElement.ValueKind is JsonValueKind.String
            ? document.RootElement.GetString()
            : document.RootElement.GetRawText();
    }

    private static Task UpsertStateStringAsync(ISyncStateRepository repository, string key, string value, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        return repository.UpsertAsync(new SyncState(Guid.NewGuid().ToString("N"), key, JsonSerializer.Serialize(value, JsonOptions), now), ct);
    }
}

public sealed record EnrolmentRedeemRequest(string ActivationKey);