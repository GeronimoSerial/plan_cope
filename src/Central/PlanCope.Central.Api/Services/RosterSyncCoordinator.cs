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

public sealed record RosterSyncRunStatus(
    bool Enabled,
    bool Running,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    RosterSyncAllResult? LastRun,
    string? LastError);

/// <summary>Runs roster refreshes without sharing a scoped database context across schools.</summary>
public sealed class RosterSyncCoordinator(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<RosterSyncCoordinator> logger)
{
    private readonly object _statusLock = new();
    private RosterSyncRunStatus _status = new(false, false, null, null, null, null);

    public bool IsEnabled =>
        string.Equals(AsistenciasRosterSource.ResolveSource(configuration), "Asistencias", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(configuration.GetConnectionString("Asistencias"));

    public RosterSyncRunStatus GetStatus()
    {
        lock (_statusLock) return _status with { Enabled = IsEnabled };
    }

    public Task<RosterSyncAllResult> SyncAllAsync(CancellationToken cancellationToken = default) =>
        RunAsync(onlyMissing: false, cancellationToken);

    public Task<RosterSyncAllResult> SyncMissingAsync(CancellationToken cancellationToken = default) =>
        RunAsync(onlyMissing: true, cancellationToken);

    /// <summary>Schedules an admin-triggered run independently of the HTTP request lifetime.</summary>
    public RosterSyncRunStatus StartBackgroundSync()
    {
        if (!IsEnabled) return GetStatus();

        lock (_statusLock)
        {
            if (_status.Running) return _status;
            _status = _status with { Enabled = true, Running = true, StartedAt = DateTimeOffset.UtcNow, LastError = null };
        }

        _ = Task.Run(async () =>
        {
            try { await ExecuteClaimedAsync(onlyMissing: false, CancellationToken.None); }
            catch (Exception exception) { logger.LogError(exception, "Admin-triggered all-school roster sync failed."); }
        });
        return GetStatus();
    }

    private async Task<RosterSyncAllResult> RunAsync(bool onlyMissing, CancellationToken cancellationToken)
    {
        var schoolYear = AsistenciasRosterSource.ResolveSchoolYear(configuration);
        if (!IsEnabled) return EmptyResult(enabled: false, schoolYear);
        lock (_statusLock)
        {
            if (_status.Running) return EmptyResult(enabled: true, schoolYear, busy: true);
            _status = _status with { Enabled = true, Running = true, StartedAt = DateTimeOffset.UtcNow, LastError = null };
        }
        return await ExecuteClaimedAsync(onlyMissing, cancellationToken);
    }

    private async Task<RosterSyncAllResult> ExecuteClaimedAsync(bool onlyMissing, CancellationToken cancellationToken)
    {
        var schoolYear = AsistenciasRosterSource.ResolveSchoolYear(configuration);
        RosterSyncAllResult? result = null;
        string? error = null;
        try
        {
            result = await SyncSchoolsAsync(schoolYear, onlyMissing, cancellationToken);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            error = exception.Message;
            logger.LogError(exception, "All-school roster sync failed before individual school processing.");
            result = EmptyResult(enabled: true, schoolYear) with { Failed = 1 };
            return result;
        }
        finally
        {
            lock (_statusLock)
                _status = _status with
                {
                    Enabled = IsEnabled,
                    Running = false,
                    CompletedAt = DateTimeOffset.UtcNow,
                    LastRun = result,
                    LastError = error
                };
        }
    }

    private async Task<RosterSyncAllResult> SyncSchoolsAsync(string schoolYear, bool onlyMissing, CancellationToken cancellationToken)
    {
        List<(long Cue, int? Annex)> schoolRows;
        HashSet<string> existingCues;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlanCopeDbContext>();
            var rows = await db.Schools.AsNoTracking()
                .Select(static school => new { school.Cue, school.Annex })
                .ToListAsync(cancellationToken);
            schoolRows = rows.Select(static school => (school.Cue, school.Annex)).ToList();
            existingCues = (await db.GeRosterSnapshots.AsNoTracking()
                    .Where(snapshot => snapshot.SchoolYear == schoolYear)
                    .Select(static snapshot => snapshot.Cue)
                    .Distinct()
                    .ToListAsync(cancellationToken))
                .ToHashSet(StringComparer.Ordinal);
        }

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
        if (onlyMissing) cues.RemoveWhere(existingCues.Contains);

        var succeeded = 0;
        var created = 0;
        var empty = 0;
        var failed = 0;
        foreach (var cue in cues)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // Each school owns a fresh scoped DbContext. A failed save cannot poison the
                // next school's ChangeTracker, and its roster graph is released immediately.
                await using var schoolScope = scopeFactory.CreateAsyncScope();
                var rosterService = schoolScope.ServiceProvider.GetRequiredService<IGeRosterService>();
                var refresh = await rosterService.RefreshAsync(cue, schoolYear, cancellationToken);
                succeeded++;
                if (refresh.Created) created++;
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

    private static RosterSyncAllResult EmptyResult(bool enabled, string schoolYear, bool busy = false) =>
        new(enabled, busy, schoolYear, 0, 0, 0, 0, 0, 0);
}
