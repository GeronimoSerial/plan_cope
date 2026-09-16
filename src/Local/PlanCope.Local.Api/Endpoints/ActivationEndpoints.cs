using Microsoft.Extensions.Options;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Local.Api.Services;
using PlanCope.RosterCrypto;
using PlanCope.Shared.Domain.Local;

namespace PlanCope.Local.Api.Endpoints;

public static class ActivationEndpoints
{
    public static IEndpointRouteBuilder MapActivationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/activation");

        group.MapGet("/status", async (INodeIdentityRepository repository, CancellationToken ct) =>
        {
            var identity = await repository.GetAsync(ct);
            return Results.Ok(new
            {
                phaseAComplete = identity is not null,
                cue = identity?.Cue,
                isLocked = identity?.RevocationStage == "locked"
            });
        });

        group.MapGet("/bundle-cues", async (IOptions<RosterBundleOptions> options, CancellationToken ct) =>
        {
            var path = options.Value.Path;
            if (string.IsNullOrWhiteSpace(path))
            {
                return Results.Ok(new { cues = Array.Empty<string>() });
            }
            var cues = await EnvelopeDecryption.ListCuesAsync(path, ct);
            return Results.Ok(new { cues });
        });

        group.MapPost("/unlock", async (
            ActivationUnlockRequest request,
            EmbeddedRosterSeeder seeder,
            HardwareFingerprintService fingerprintService,
            INodeIdentityRepository nodeIdentityRepository,
            CancellationToken ct) =>
        {
            try
            {
                await seeder.SeedOneAsync(request.Cue, request.Passphrase, ct);
            }
            catch (Exception ex) when (ex is RosterEntryNotFoundException or System.Security.Cryptography.CryptographicException)
            {
                return Results.BadRequest(new { error = "La frase secreta o el CUE ingresado no son válidos." });
            }

            var fingerprint = fingerprintService.ComputeFingerprint();
            var existing = await nodeIdentityRepository.GetAsync(ct);
            var identity = new NodeIdentity(
                Id: existing?.Id ?? Guid.NewGuid().ToString("N"),
                NodeId: existing?.NodeId,
                Cue: request.Cue,
                FingerprintHash: fingerprint.CompositeHash,
                FingerprintComponentsJson: fingerprint.ComponentsJson,
                EnrolledAt: existing?.EnrolledAt,
                LastSyncAt: existing?.LastSyncAt,
                CredentialState: existing?.CredentialState ?? "unenrolled",
                RevocationDetectedAt: existing?.RevocationDetectedAt,
                RevocationStage: existing?.RevocationStage);
            await nodeIdentityRepository.UpsertAsync(identity, ct);

            return Results.Ok(new { cue = request.Cue });
        });

        return endpoints;
    }
}

public sealed record ActivationUnlockRequest(string Passphrase, string Cue);