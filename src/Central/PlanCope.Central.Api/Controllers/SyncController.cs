using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FluentValidation;
using Npgsql;
using PlanCope.Central.Api.Data;
using PlanCope.Central.Api.Services;
using PlanCope.Central.Api.Sync;
using PlanCope.Shared.Contracts.Exams;
using PlanCope.Shared.Contracts.Sync;
using PlanCope.Shared.Domain.Central;
using PlanCope.Shared.Domain.Local;
using PlanCope.Shared.Domain.ValueObjects;
using PlanCope.Shared.Grading;
using GradingExamVersion = PlanCope.Shared.Grading.ExamVersion;

namespace PlanCope.Central.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/sync")]
public sealed class SyncController(
    PlanCopeDbContext dbContext,
    CentralStatsRollupService statsRollupService,
    ILogger<SyncController>? logger = null,
    CentralAttemptGradingService? attemptGradingService = null) : ControllerBase
{
    private static readonly JsonSerializerOptions SyncJsonOptions = new(JsonSerializerDefaults.Web);

    [HttpGet("pull")]
    public async Task<ActionResult<PullResponse>> Pull([FromQuery] string? nodeId, [FromQuery] string? cursor, [FromQuery] int limit = 50, CancellationToken cancellationToken = default)
    {
        // The caller's node identity comes from the validated JWT, never from the query string.
        // A valid user/operator token (token_type != node_access) must not be able to read another
        // node's exams or write its cursors/delivery markers. 403, not 401: the token is valid,
        // just not privileged for this endpoint — same gate and response style as UpdatesController.
        if (!NodeAccessAuth.TryGetNodeId(User, out var claimNodeId))
        {
            return Forbid();
        }

        // nodeId stays optional for backward compatibility: nodes already installed send it. When
        // present it must echo the claim; the claim value is what every lookup and write below uses,
        // so a spoofed id can never widen delivery or poison a foreign node's cursor.
        if (!string.IsNullOrWhiteSpace(nodeId) &&
            !string.Equals(nodeId.Trim(), claimNodeId, StringComparison.Ordinal))
        {
            return Forbid();
        }

        var normalizedNodeId = claimNodeId;
        var registeredNode = await dbContext.RegisteredNodes.AsNoTracking()
            .SingleOrDefaultAsync(node => node.Id == normalizedNodeId, cancellationToken);
        if (registeredNode?.RevokedAt is not null)
        {
            return Forbid();
        }
        var normalizedLimit = Math.Clamp(limit, 1, 200);
        // Materialize the cursor as a DateTimeOffset so the comparison stays translatable to SQL:
        // EF cannot translate `x.PublishedAt.Value.UtcTicks` and would throw on real PostgreSQL.
        var cursorInstant = new DateTimeOffset(ParseCursor(cursor), TimeSpan.Zero);
        var candidates = await dbContext.PublicationPackages
            .Where(x => x.Status == "Published" && x.PublishedAt != null && x.PublishedAt > cursorInstant)
            .OrderBy(x => x.PublishedAt)
            .ThenBy(x => x.Id)
            .Take(normalizedLimit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = candidates.Count > normalizedLimit;
        var page = candidates.Take(normalizedLimit).ToList();

        // Every published package is available to every node, so the cursor advances through the
        // page returned by the package query.
        var nextCursor = page.Count == 0
            ? (cursor ?? "0")
            : (page[^1].PublishedAt ?? page[^1].CreatedAt).UtcTicks.ToString();

        var items = new List<SyncItem>(page.Count);
        var checksums = new Dictionary<string, string>(StringComparer.Ordinal);
        var deliveredPackageIds = new List<string>(page.Count);

        foreach (var package in page)
        {
            var payload = await BuildPayloadAsync(package, cancellationToken);
            items.Add(new SyncItem(
                "publication_package",
                package.Id,
                "upsert",
                JsonSerializer.SerializeToElement(payload),
                (package.PublishedAt ?? package.CreatedAt).ToString("O"),
                package.Checksum));
            checksums[package.Id] = package.Checksum;
            deliveredPackageIds.Add(package.Id);
        }

        await RecordPullProgressAsync(normalizedNodeId, nextCursor, deliveredPackageIds, cancellationToken);

        return Ok(new PullResponse(items, nextCursor, hasMore, checksums));
    }

    [HttpPost("push")]
    public async Task<ActionResult<PushResponse>> Push(
        [FromBody] PushRequest? request,
        [FromHeader(Name = "X-Node-Id")] string? nodeHeader,
        [FromServices] IValidator<PushRequest> validator,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return BadRequest(new { error = "A push request is required." });
        }

        // Same identity gate as Pull: the token must be a node-access token before any
        // client-supplied id is trusted. 403, not 401 — see Pull.
        if (!NodeAccessAuth.TryGetNodeId(User, out var claimNodeId))
        {
            return Forbid();
        }

        // Keep the existing 400 for a missing header or a header/body mismatch; only once the pair
        // is internally consistent do we check it against the token claim.
        if (string.IsNullOrWhiteSpace(nodeHeader) ||
            !string.Equals(nodeHeader.Trim(), request.NodeId?.Trim(), StringComparison.Ordinal))
        {
            return BadRequest(new { error = "X-Node-Id must match nodeId." });
        }

        // A consistent header/body pair that does not match the claim is a spoofed node id.
        if (!string.Equals(nodeHeader.Trim(), claimNodeId, StringComparison.Ordinal))
        {
            return Forbid();
        }

        // Deliberately do not reject a revoked node here. Results already collected on that
        // device must remain deliverable after revocation; revocation blocks new pulls/redeems.

        if (request.Items.Count > 200)
        {
            return BadRequest(new { error = "A push request cannot contain more than 200 items." });
        }

        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        }

        var nodeId = claimNodeId;
        var results = new List<PushItemResult>(request.Items.Count);
        var keysInRequest = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in request.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!keysInRequest.Add(item.IdempotencyKey))
            {
                results.Add(new PushItemResult(item.IdempotencyKey, "failed", "Duplicate idempotency key in request."));
                continue;
            }

            var result = await AcceptItemAsync(nodeId, item, cancellationToken);
            results.Add(result);
        }

        var accepted = results.Count(static result => result.Status is "accepted" or "duplicate");
        return Ok(new PushResponse(accepted, results.Count - accepted, results));
    }

    private async Task<PushItemResult> AcceptItemAsync(string nodeId, PushItem item, CancellationToken cancellationToken, int retryCount = 0)
    {
        var existing = await dbContext.SyncInbox
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.IdempotencyKey == item.IdempotencyKey, cancellationToken);
        var policyResult = SyncPushPolicy.Evaluate(item, existing?.Payload.RootElement, existing?.CreatedAt, existing?.Status);
        if (existing is not null || policyResult.Status is not "accepted")
        {
            if (existing is not null && policyResult.Status == "duplicate" && existing.Status != "processed")
            {
                var processingStatus = await ProcessInboxByIdAsync(existing.Id, cancellationToken);
                return policyResult with { ProcessingStatus = processingStatus };
            }
            return policyResult;
        }

        try
        {
            using var payload = JsonDocument.Parse(item.Payload.GetRawText());
            if (ContainsForbiddenNominalProperty(payload.RootElement))
            {
                return new PushItemResult(item.IdempotencyKey, "failed", "The payload contains a prohibited document or token field.");
            }
            if (item.EventType is SyncEventTypes.AttemptSubmitted && !IsValidAttemptPayload(item, payload.RootElement))
            {
                return new PushItemResult(item.IdempotencyKey, "failed", "The attempt payload is invalid.");
            }

            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var receivedAt = TruncateToPostgresPrecision(DateTimeOffset.UtcNow);
            var inbox = new SyncInbox(
                Guid.NewGuid().ToString("N"),
                nodeId,
                item.EventType,
                item.AggregateType,
                item.AggregateId,
                item.IdempotencyKey,
                JsonDocument.Parse(item.Payload.GetRawText()),
                "received",
                receivedAt,
                null);
            dbContext.SyncInbox.Add(inbox);

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();

            var processingStatus = await ProcessInboxByIdAsync(inbox.Id, cancellationToken);
            return new PushItemResult(item.IdempotencyKey, "accepted", null, receivedAt, processingStatus);
        }
        catch (DbUpdateException exception)
        {
            dbContext.ChangeTracker.Clear();
            var nowExisting = await dbContext.SyncInbox
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.IdempotencyKey == item.IdempotencyKey, cancellationToken);
            if (nowExisting is null)
            {
                if (retryCount == 0 && IsUniqueViolation(exception))
                {
                    return await AcceptItemAsync(nodeId, item, cancellationToken, retryCount + 1);
                }

                return new PushItemResult(item.IdempotencyKey, "failed", "The item could not be persisted.");
            }

            return SyncPushPolicy.Evaluate(item, nowExisting.Payload.RootElement, nowExisting.CreatedAt, nowExisting.Status);
        }
        catch (JsonException exception)
        {
            dbContext.ChangeTracker.Clear();
            return new PushItemResult(item.IdempotencyKey, "failed", exception.Message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger?.LogError(exception, "Central failed to accept sync item {IdempotencyKey} from node {NodeId}.", item.IdempotencyKey, nodeId);
            dbContext.ChangeTracker.Clear();
            return new PushItemResult(item.IdempotencyKey, "failed", exception.Message);
        }
    }

    /// <summary>
    /// Processes an inbox row after its durable receipt has committed. Failures leave the row in
    /// the existing inbox with a retryable status; this avoids a second delivery queue.
    /// </summary>
    public async Task<string> ProcessInboxByIdAsync(string inboxId, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            if (dbContext.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    $"SELECT pg_advisory_xact_lock({CentralStatsRollupService.AdvisoryLockNamespace}, {CentralStatsRollupService.AdvisoryLockKey});",
                    cancellationToken);
            }

            var inbox = await dbContext.SyncInbox.SingleOrDefaultAsync(item => item.Id == inboxId, cancellationToken);
            if (inbox is null) return "missing";
            if (inbox.Status == "processed") return inbox.Status;

            if (inbox.EventType is SyncEventTypes.AttemptSubmitted)
            {
                var payload = inbox.Payload.RootElement.Clone();
                var item = new PushItem(
                    inbox.IdempotencyKey,
                    inbox.EventType,
                    inbox.AggregateType,
                    inbox.AggregateId,
                    payload,
                    SyncPayloadChecksum.Calculate(payload),
                    inbox.CreatedAt.ToString("O"));
                await AddAttemptAsync(inbox.SourceNodeId ?? string.Empty, item, payload, cancellationToken);
            }

            dbContext.Entry(inbox).CurrentValues.SetValues(inbox with
            {
                Status = "processed",
                ProcessedAt = DateTimeOffset.UtcNow,
                NextProcessingAt = null
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            return "processed";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            dbContext.ChangeTracker.Clear();
            try
            {
                var inbox = await dbContext.SyncInbox.SingleOrDefaultAsync(item => item.Id == inboxId, cancellationToken);
                if (inbox is not null)
                {
                    // Commit can succeed at PostgreSQL while its confirmation is lost. Never turn
                    // an already processed row back into retryable work or the rollup would count twice.
                    if (inbox.Status == "processed") return "processed";
                    var attemptCount = inbox.ProcessingAttemptCount + 1;
                    var delaySeconds = Math.Min(3600, 30 * Math.Pow(2, Math.Min(attemptCount - 1, 7)));
                    if (dbContext.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL")
                    {
                        // Another same-key retry can acquire the advisory lock and finish after
                        // this failed transaction releases it but before this catch runs. Make
                        // the failure mark conditional so it cannot overwrite that processed row.
                        await dbContext.SyncInbox
                            .Where(row => row.Id == inboxId && row.Status != "processed")
                            .ExecuteUpdateAsync(update => update
                                .SetProperty(row => row.Status, "processing_failed")
                                .SetProperty(row => row.ProcessedAt, (DateTimeOffset?)null)
                                .SetProperty(row => row.ProcessingAttemptCount, row => row.ProcessingAttemptCount + 1)
                                .SetProperty(row => row.NextProcessingAt, DateTimeOffset.UtcNow.AddSeconds(delaySeconds)),
                                cancellationToken);
                    }
                    else
                    {
                        dbContext.Entry(inbox).CurrentValues.SetValues(inbox with
                        {
                            Status = "processing_failed",
                            ProcessedAt = null,
                            ProcessingAttemptCount = attemptCount,
                            NextProcessingAt = DateTimeOffset.UtcNow.AddSeconds(delaySeconds)
                        });
                        await dbContext.SaveChangesAsync(cancellationToken);
                    }
                }
            }
            catch (Exception statusException) when (statusException is not OperationCanceledException)
            {
                logger?.LogWarning("Could not store sync processing status for inbox row {InboxId}; failure type {ErrorType}.",
                    inboxId, statusException.GetType().Name);
            }

            logger?.LogWarning("Sync inbox row {InboxId} is durable but processing failed with {ErrorType}; it remains retryable.",
                inboxId, exception.GetType().Name);
            dbContext.ChangeTracker.Clear();
            var currentStatus = await dbContext.SyncInbox.AsNoTracking()
                .Where(item => item.Id == inboxId)
                .Select(item => item.Status)
                .SingleOrDefaultAsync(cancellationToken);
            return currentStatus ?? "processing_failed";
        }
    }

    private async Task AddAttemptAsync(string nodeId, PushItem item, JsonElement payload, CancellationToken cancellationToken)
    {
        if (!payload.TryGetProperty("attempt", out var attemptElement))
        {
            throw new JsonException("attempt_submitted payload must contain an attempt.");
        }

        var attempt = attemptElement.Deserialize<StudentAttempt>(SyncJsonOptions)
            ?? throw new JsonException("The attempt payload is invalid.");
        if (!string.Equals(attempt.Id, item.AggregateId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(attempt.StudentCode))
        {
            throw new JsonException("The attempt aggregate does not match the payload.");
        }

        DateTimeOffset? startedAt = ParseOptionalDate(attempt.StartedAt);
        DateTimeOffset? submittedAt = ParseOptionalDate(attempt.SubmittedAt);
        DateTimeOffset? verifiedAt = ParseOptionalDate(attempt.VerifiedAt);
        var receivedAt = DateTimeOffset.UtcNow;
        var receivedAttemptId = Guid.NewGuid().ToString("N");
        var centralSessionId = UpsertDeliverySession(nodeId, payload, receivedAt);
        dbContext.ReceivedStudentAttempts.Add(new ReceivedStudentAttempt(
            receivedAttemptId,
            attempt.Id,
            centralSessionId ?? attempt.DeliverySessionId,
            attempt.StudentCode,
            attempt.Status,
            startedAt,
            submittedAt,
            receivedAt,
            item.IdempotencyKey,
            receivedAt,
            attempt.GePersonId,
            attempt.RosterStudentId,
            ReadOptionalString(payload, "rosterSnapshotId"),
            ReadOptionalString(payload, "rosterSectionId"),
            attempt.StudentFirstName,
            attempt.StudentLastName,
            attempt.DocumentLast4,
            attempt.VerificationSource,
            verifiedAt,
            attempt.DocumentHmac,
            attempt.OffRoster));

        var receivedAnswers = new List<ReceivedSubmissionAnswer>();
        if (payload.TryGetProperty("answers", out var answersElement) && answersElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var answerElement in answersElement.EnumerateArray())
            {
                var answer = answerElement.Deserialize<SubmissionAnswer>(SyncJsonOptions)
                    ?? throw new JsonException("The answer payload is invalid.");
                if (string.IsNullOrWhiteSpace(answer.BlockId))
                {
                    throw new JsonException("An answer is missing its block id.");
                }

                var receivedAnswer = new ReceivedSubmissionAnswer(
                    Guid.NewGuid().ToString("N"),
                    attempt.Id,
                    answer.BlockId,
                    JsonDocument.Parse(answer.AnswerJson),
                    receivedAt);
                dbContext.ReceivedSubmissionAnswers.Add(receivedAnswer);
                receivedAnswers.Add(receivedAnswer);
            }
        }

        var examVersionRemoteId = ReadOptionalString(payload, "examVersionRemoteId");
        var grader = attemptGradingService ?? new CentralAttemptGradingService(dbContext, statsRollupService, logger: null);
        await grader.RecomputeAsync(receivedAttemptId, examVersionRemoteId, receivedAnswers, cancellationToken);
    }

    private static bool IsValidAttemptPayload(PushItem item, JsonElement payload)
    {
        if (!payload.TryGetProperty("attempt", out var attemptElement) || attemptElement.ValueKind is not JsonValueKind.Object)
            return false;
        var attempt = attemptElement.Deserialize<StudentAttempt>(SyncJsonOptions);
        if (attempt is null || !string.Equals(attempt.Id, item.AggregateId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(attempt.StudentCode))
            return false;
        if (!payload.TryGetProperty("answers", out var answersElement) || answersElement.ValueKind is not JsonValueKind.Array)
            return false;
        foreach (var answerElement in answersElement.EnumerateArray())
        {
            var answer = answerElement.Deserialize<SubmissionAnswer>(SyncJsonOptions);
            if (answer is null || string.IsNullOrWhiteSpace(answer.BlockId)) return false;
            try
            {
                using var answerJson = JsonDocument.Parse(answer.AnswerJson);
            }
            catch (JsonException)
            {
                return false;
            }
        }
        return true;
    }

    private string? UpsertDeliverySession(string nodeId, JsonElement payload, DateTimeOffset receivedAt)
    {
        if (!payload.TryGetProperty("deliverySession", out var sessionElement) ||
            sessionElement.ValueKind is not JsonValueKind.Object)
        {
            return null;
        }

        var id = ReadOptionalString(sessionElement, "id");
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var schoolCueValue = ReadOptionalString(sessionElement, "schoolCue");
        var schoolCue = CueCode.TryNormalize(schoolCueValue, out var normalizedCue) ? normalizedCue : null;
        var startedAt = ParseOptionalDate(ReadOptionalString(sessionElement, "startedAt"));
        var closedAt = ParseOptionalDate(ReadOptionalString(sessionElement, "closedAt"));
        var existing = dbContext.DeliverySessions.Local.FirstOrDefault(session =>
            session.SourceNodeId == nodeId && session.RemoteLocalId == id);
        existing ??= dbContext.DeliverySessions.SingleOrDefault(session =>
            session.SourceNodeId == nodeId && session.RemoteLocalId == id);
        var incomingStatus = ReadOptionalString(sessionElement, "status");
        var closed = existing?.Status.Equals("closed", StringComparison.OrdinalIgnoreCase) == true ||
            closedAt is not null || string.Equals(incomingStatus, "closed", StringComparison.OrdinalIgnoreCase);
        var session = new CentralDeliverySession(
            existing?.Id ?? Guid.NewGuid().ToString("N"),
            id,
            existing?.SchoolId ?? schoolCue,
            existing?.ExamVersionId ?? ReadOptionalString(sessionElement, "examVersionId"),
            existing?.ClassroomCode ?? ReadOptionalString(sessionElement, "classroomCode"),
            existing?.CommissionCode ?? ReadOptionalString(sessionElement, "commissionCode"),
            closed ? "closed" : existing?.Status ?? incomingStatus ?? "open",
            existing?.StartedAt ?? startedAt,
            existing?.EndedAt ?? closedAt,
            receivedAt,
            existing?.CreatedAt ?? receivedAt,
            nodeId,
            existing?.SchoolYear ?? ReadOptionalString(sessionElement, "schoolYear"),
            existing?.Course ?? ReadOptionalString(sessionElement, "course"),
            existing?.RosterSectionId ?? ReadOptionalString(sessionElement, "sectionId"),
            existing?.JoinedCount ?? 0,
            existing?.InProgressCount ?? 0,
            existing?.SubmittedCount ?? 0,
            existing?.ClosedOrForcedCount ?? 0,
            existing?.LastActivityAt,
            existing?.LastHeartbeatAt,
            existing?.LocalAppVersion);
        if (existing is null)
        {
            dbContext.DeliverySessions.Add(session);
        }
        else
        {
            dbContext.Entry(existing).CurrentValues.SetValues(session);
        }

        return session.Id;
    }

    private static bool IsUniqueViolation(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }) return true;
        }

        return false;
    }

    private static DateTimeOffset TruncateToPostgresPrecision(DateTimeOffset value) =>
        new(value.UtcTicks - value.UtcTicks % 10, TimeSpan.Zero);

    private static DateTimeOffset? ParseOptionalDate(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    private static string? ReadOptionalString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool ContainsForbiddenNominalProperty(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (IsForbiddenPropertyName(property.Name))
                {
                    return true;
                }

                if (ContainsForbiddenNominalProperty(property.Value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().Any(ContainsForbiddenNominalProperty);
        }

        return false;
    }

    private static bool IsForbiddenPropertyName(string name) =>
        name.Equals("document", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("documentHash", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("token", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("tokenHash", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("resolutionToken", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("dni", StringComparison.OrdinalIgnoreCase);

    private async Task<PublishedExamPackageDto> BuildPayloadAsync(PublicationPackage package, CancellationToken cancellationToken)
    {
        var version = await dbContext.ExamVersions.SingleAsync(x => x.Id == package.ExamVersionId, cancellationToken);
        var exam = await dbContext.Exams.SingleAsync(x => x.Id == version.ExamId, cancellationToken);
        var blocks = await dbContext.ExamBlocks
            .Where(x => x.ExamVersionId == version.Id)
            .OrderBy(x => x.OrderIndex)
            .ToListAsync(cancellationToken);
        var blockIds = blocks.Select(static x => x.Id).ToList();
        var answerKeys = await dbContext.AnswerKeys
            .Where(x => blockIds.Contains(x.ExamBlockId))
            .OrderBy(x => x.ExamBlockId)
            .ToListAsync(cancellationToken);
        var referencedAssetIds = ExamPackageChecksum.GetReferencedImageAssetIds(blocks.Select(static block => block.Config.RootElement));
        var assets = await dbContext.ExamAssets
            .Where(x => x.ExamVersionId == version.Id && referencedAssetIds.Contains(x.Id))
            .OrderBy(x => x.FileName)
            .ToListAsync(cancellationToken);
        var targets = await dbContext.PublicationTargets
            .Where(x => x.PublicationPackageId == package.Id)
            .OrderBy(x => x.TargetType)
            .ThenBy(x => x.TargetId)
            .ToListAsync(cancellationToken);

        return new PublishedExamPackageDto(
            package.Id,
            exam.Id,
            version.Id,
            exam.Code,
            exam.Title,
            version.VersionNumber,
            version.SchemaVersion,
            package.Checksum,
            ToJsonElement(version.Metadata),
            blocks.Select(ToDto).ToList(),
            answerKeys.Select(ToDto).ToList(),
            assets.Select(ToPublishedDto).ToList(),
            targets.Select(static target => new PublicationTargetDto(target.TargetType, target.TargetId)).ToList(),
            ScoringPolicy: "AllOrNothing");
    }

    /// <summary>
    /// Persists the node's exam-pull cursor plus one delivery marker per package actually handed
    /// over on this call. Markers are keyed by (nodeId, "package:{packageId}") and power
    /// <c>pulledByNodeCount</c> on the exam summary.
    /// </summary>
    private async Task RecordPullProgressAsync(
        string nodeId,
        string nextCursor,
        IReadOnlyList<string> deliveredPackageIds,
        CancellationToken cancellationToken)
    {
        var deliveryKeys = deliveredPackageIds.Select(SyncCursorKeys.PackageDelivery).ToList();
        var existing = await dbContext.SyncCursors
            .Where(cursor =>
                cursor.NodeId == nodeId &&
                (cursor.CursorKey == SyncCursorKeys.ExamPull || deliveryKeys.Contains(cursor.CursorKey)))
            .ToListAsync(cancellationToken);
        var existingByKey = existing.ToDictionary(static cursor => cursor.CursorKey, StringComparer.Ordinal);
        var now = DateTimeOffset.UtcNow;

        UpsertCursor(existingByKey, nodeId, SyncCursorKeys.ExamPull, nextCursor, now);
        foreach (var packageId in deliveredPackageIds)
        {
            UpsertCursor(existingByKey, nodeId, SyncCursorKeys.PackageDelivery(packageId), "delivered", now);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Pull tracking is best-effort telemetry: a concurrent pull of the same node must never
            // turn a successful delivery into a failed pull.
            dbContext.ChangeTracker.Clear();
        }
    }

    private void UpsertCursor(
        IReadOnlyDictionary<string, SyncCursor> existingByKey,
        string nodeId,
        string cursorKey,
        string cursorValue,
        DateTimeOffset now)
    {
        if (existingByKey.TryGetValue(cursorKey, out var existing))
        {
            dbContext.Entry(existing).CurrentValues.SetValues(existing with { CursorValue = cursorValue, UpdatedAt = now });
            return;
        }

        dbContext.SyncCursors.Add(new SyncCursor(Guid.NewGuid().ToString("N"), nodeId, cursorKey, cursorValue, now));
    }

    /// <summary>
    /// Parses the opaque <c>UtcTicks</c> pull cursor. Anything that is not a number, is negative, or
    /// exceeds <see cref="DateTimeOffset.MaxValue"/> is treated as "from the beginning" (0) so a
    /// stale or hostile cursor can never throw while constructing the <see cref="DateTimeOffset"/>.
    /// </summary>
    private static long ParseCursor(string? cursor)
    {
        return long.TryParse(cursor, out var ticks) && ticks >= 0 && ticks <= DateTimeOffset.MaxValue.UtcTicks
            ? ticks
            : 0;
    }

    private static BlockDto ToDto(ExamBlock block)
    {
        return new BlockDto(
            block.Id,
            block.ExamVersionId,
            block.OrderIndex,
            block.BlockType,
            block.Title,
            block.Description,
            block.Config.RootElement.Clone(),
            block.Validation?.RootElement.Clone());
    }

    private static AnswerKeyDto ToDto(AnswerKey answerKey)
    {
        return new AnswerKeyDto(
            answerKey.Id,
            answerKey.ExamBlockId,
            answerKey.CorrectAnswer.RootElement.Clone(),
            answerKey.ScoreValue,
            answerKey.Metadata?.RootElement.Clone());
    }

    private static PublishedAssetDto ToPublishedDto(ExamAsset asset)
    {
        var contentBase64 = asset.StoragePath.StartsWith("base64:", StringComparison.Ordinal)
            ? asset.StoragePath["base64:".Length..]
            : string.Empty;

        return new PublishedAssetDto(asset.Id, asset.ExamVersionId, asset.FileName, asset.MimeType, asset.SizeBytes, asset.Checksum, contentBase64);
    }

    private static JsonElement? ToJsonElement(JsonDocument? document)
    {
        return document?.RootElement.Clone();
    }
}
