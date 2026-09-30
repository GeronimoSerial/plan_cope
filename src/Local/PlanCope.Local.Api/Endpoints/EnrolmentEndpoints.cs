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
            LocalExamPullService examPullService,
            HardwareFingerprintService fingerprintService,
            CancellationToken ct) =>
        {
            var identity = await nodeIdentityRepository.GetAsync(ct);

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
                string.Empty,
                AppVersion: null);

            var response = await client.PostAsJsonAsync("api/activation/redeem", redeemRequest, ct);

            var body = await response.Content.ReadAsStringAsync(ct);
            var parsed = ParseRedeemBody(body);

            if (!response.IsSuccessStatusCode)
            {
                var message = parsed.FailureReason is { } failureReason
                    ? FailureMessage(failureReason)
                    : "No se pudo validar la clave de activación. Verificá que sea correcta y no esté vencida o revocada.";
                return Results.BadRequest(new { error = message });
            }

            if (parsed.FailureReason is { } reportedFailure)
            {
                return Results.BadRequest(new { error = FailureMessage(reportedFailure) });
            }

            if (parsed.Response is not { } redeemed)
            {
                return Results.BadRequest(new { error = "Central devolvió una respuesta inválida." });
            }

            await UpsertStateStringAsync(syncStateRepository, "node_id", redeemed.NodeId, ct);
            await UpsertStateStringAsync(syncStateRepository, "central_access_token", redeemed.AccessToken, ct);
            await UpsertStateStringAsync(syncStateRepository, "central_refresh_token", redeemed.RefreshToken, ct);
            await UpsertStateStringAsync(syncStateRepository, "central_access_token_expires_at", redeemed.AccessTokenExpiresAt.ToString("O"), ct);
            await UpsertStateStringAsync(syncStateRepository, "central_refresh_token_expires_at", redeemed.RefreshTokenExpiresAt.ToString("O"), ct);

            var enrolledIdentity = identity ?? new NodeIdentity(
                Guid.NewGuid().ToString("N"), null, null, fingerprint.CompositeHash, fingerprint.ComponentsJson,
                null, null, "active", null, null);
            await nodeIdentityRepository.UpsertAsync(enrolledIdentity with
            {
                NodeId = redeemed.NodeId,
                Cue = null,
                EnrolledAt = DateTimeOffset.UtcNow.ToString("O"),
                CredentialState = "active"
            }, ct);

            // Establish the initial exam package cache before the host opens the workspace.
            // The persisted credentials make a retry safe if Central is temporarily unavailable.
            var initialPull = await examPullService.PullAsync(ct);
            if (!initialPull.Success)
            {
                return Results.Problem("La activación se guardó, pero no se pudieron descargar los datos iniciales. Reintentá cuando vuelva la conexión.",
                    statusCode: StatusCodes.Status502BadGateway);
            }

            return Results.Ok(new { nodeId = redeemed.NodeId });
        });

        return endpoints;
    }

    /// <summary>
    /// Tolerant reader for the Central redeem body. Accepts the current
    /// <see cref="ActivationRedeemResult"/> wrapper, the legacy bare
    /// <see cref="ActivationRedeemResponse"/> shape, and rejects anything malformed without
    /// throwing so a bad Central body can never surface as a 500.
    /// </summary>
    private static RedeemBody ParseRedeemBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return RedeemBody.Invalid;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return RedeemBody.Invalid;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return RedeemBody.Invalid;
            }

            if (root.TryGetProperty("isSuccess", out var isSuccess) &&
                isSuccess.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                if (!isSuccess.GetBoolean())
                {
                    return new RedeemBody(null, ReadFailureReason(root));
                }

                return root.TryGetProperty("response", out var responseElement)
                    ? new RedeemBody(ReadResponse(responseElement), null)
                    : RedeemBody.Invalid;
            }

            // Legacy shape: the credential fields sit at the root with no wrapper.
            return new RedeemBody(ReadResponse(root), null);
        }
    }

    private static ActivationRedeemResponse? ReadResponse(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!TryReadString(element, "nodeId", out var nodeId) ||
            !TryReadString(element, "accessToken", out var accessToken) ||
            !TryReadString(element, "refreshToken", out var refreshToken) ||
            !TryReadDateTimeOffset(element, "accessTokenExpiresAt", out var accessTokenExpiresAt) ||
            !TryReadDateTimeOffset(element, "refreshTokenExpiresAt", out var refreshTokenExpiresAt))
        {
            return null;
        }

        return new ActivationRedeemResponse
        {
            NodeId = nodeId,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            AccessTokenExpiresAt = accessTokenExpiresAt,
            RefreshTokenExpiresAt = refreshTokenExpiresAt,
        };
    }

    private static bool TryReadString(JsonElement element, string property, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(property, out var propertyValue) || propertyValue.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var text = propertyValue.GetString();
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        value = text;
        return true;
    }

    private static bool TryReadDateTimeOffset(JsonElement element, string property, out DateTimeOffset value)
    {
        value = default;
        return element.TryGetProperty(property, out var propertyValue) &&
            propertyValue.ValueKind == JsonValueKind.String &&
            propertyValue.TryGetDateTimeOffset(out value);
    }

    private static ActivationRedeemFailureReason? ReadFailureReason(JsonElement root)
    {
        if (!root.TryGetProperty("reason", out var reason))
        {
            return null;
        }

        if (reason.ValueKind == JsonValueKind.String &&
            Enum.TryParse<ActivationRedeemFailureReason>(reason.GetString(), ignoreCase: true, out var named))
        {
            return named;
        }

        if (reason.ValueKind == JsonValueKind.Number &&
            reason.TryGetInt32(out var numeric) &&
            Enum.IsDefined(typeof(ActivationRedeemFailureReason), numeric))
        {
            return (ActivationRedeemFailureReason)numeric;
        }

        return null;
    }

    private static string FailureMessage(ActivationRedeemFailureReason reason) => reason switch
    {
        ActivationRedeemFailureReason.MalformedKey => "La clave de activación tiene un formato inválido.",
        ActivationRedeemFailureReason.KeyNotFound => "La clave de activación no existe.",
        ActivationRedeemFailureReason.KeyRevoked => "La clave de activación fue revocada.",
        ActivationRedeemFailureReason.KeyExpired => "La clave de activación está vencida.",
        ActivationRedeemFailureReason.ActivationLimitReached => "La clave de activación alcanzó su límite de usos.",
        ActivationRedeemFailureReason.FingerprintCollision => "La identidad del equipo ya está asociada a otra inscripción.",
        _ => "No se pudo validar la clave de activación. Verificá que sea correcta y no esté vencida o revocada.",
    };

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

    private readonly record struct RedeemBody(
        ActivationRedeemResponse? Response,
        ActivationRedeemFailureReason? FailureReason)
    {
        public static RedeemBody Invalid => new(null, null);
    }
}

public sealed record EnrolmentRedeemRequest(string ActivationKey);
