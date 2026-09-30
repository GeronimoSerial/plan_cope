using Microsoft.EntityFrameworkCore;
using PlanCope.Central.Api.Data;
using PlanCope.Central.Api.Integrations.Ge;
using PlanCope.Shared.Domain.ValueObjects;

namespace PlanCope.Central.Api.Services;

public sealed record RosterSyncAllResult(
    bool Enabled,
    bool Busy,
    string SchoolYear,
    int TotalSchools,
    int Succeeded,
    int Created,
    int Empty,
    int Failed,
    int InvalidSchools);

/// <summary>Runs the shared all-school refresh operation for startup, schedule, and admin requests.</summary>
public sealed class RosterSyncCoordinator(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<RosterSyncCoordinator> logger)
{
    private int _running;
    public bool IsEnabled => !string.IsNullOrWhiteSpace(configuration.GetConnectionString("Asistencias"));

    public async Task<bool> HasAnySnapshotAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanCopeDbContext>();
        var schoolYear = AsistenciasRosterSource.ResolveSchoolYear(configuration);
        return await db.GeRosterSnapshots.AsNoTracking().AnyAsync(snapshot => snapshot.SchoolYear == schoolYear, cancellationToken);
    }

    public async Task<RosterSyncAllResult> SyncAllAsync(CancellationToken cancellationToken = default)
    {
        var schoolYear = AsistenciasRosterSource.ResolveSchoolYear(configuration);
        if (!IsEnabled)
            return new(false, false, schoolYear, 0, 0, 0, 0, 0, 0);
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            return new(true, true, schoolYear, 0, 0, 0, 0, 0, 0);

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<PlanCopeDbContext>();
            var rosterService = scope.ServiceProvider.GetRequiredService<IGeRosterService>();
            var schoolRows = await db.Schools.AsNoTracking()
                .Select(static school => new { school.Cue, school.Annex })
                .ToListAsync(cancellationToken);
            var invalidSchools = 0;
            var cues = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var school in schoolRows)
            {
                if (CueCode.TryFromSchool(school.Cue, school.Annex, out var cue)) cues.Add(cue);
                else
                {
                    invalidSchools++;
                    logger.LogWarning("Skipping Central school row with invalid stored CUE {StoredCue} and annex {Annex}.", school.Cue, school.Annex);
                }
            }

            var succeeded = 0;
            var created = 0;
            var empty = 0;
            var failed = 0;
            foreach (var cue in cues)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var result = await rosterService.RefreshAsync(cue, schoolYear, cancellationToken);
                    succeeded++;
                    if (result.Created) created++;
                }
                catch (GeRosterEmptyException exception)
                {
                    empty++;
                    logger.LogWarning(exception, "No roster rows were available for CUE {Cue} and school year {SchoolYear}; continuing.", cue, schoolYear);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception exception)
                {
                    failed++;
                    logger.LogError(exception, "Roster sync failed for CUE {Cue} and school year {SchoolYear}; continuing.", cue, schoolYear);
                }
            }

            var summary = new RosterSyncAllResult(true, false, schoolYear, cues.Count, succeeded, created, empty, failed, invalidSchools);
            logger.LogInformation("Roster sync completed for school year {SchoolYear}: {Succeeded}/{TotalSchools} succeeded, {Created} new, {Empty} empty, {Failed} failed, {InvalidSchools} invalid school rows.",
                schoolYear, succeeded, cues.Count, created, empty, failed, invalidSchools);
            return summary;
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }
}
