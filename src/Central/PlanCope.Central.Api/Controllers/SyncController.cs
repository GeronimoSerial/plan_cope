using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using FluentValidation;
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
public sealed class SyncController(PlanCopeDbContext dbContext, PlanCope.Central.Api.Services.CentralStatsRollupService statsRollupService) : ControllerBase
{
    private static readonly JsonSerializerOptions SyncJsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly JsonSerializerOptions BlocksJsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

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
        var schoolIds = await ResolveNodeSchoolIdsAsync(normalizedNodeId, cancellationToken);

        var candidates = await dbContext.PublicationPackages
            .Where(x => x.Status == "Published" && x.PublishedAt != null && x.PublishedAt > cursorInstant)
            .OrderBy(x => x.PublishedAt)
            .ThenBy(x => x.Id)
            .Take(normalizedLimit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = candidates.Count > normalizedLimit;
        var page = candidates.Take(normalizedLimit).ToList();

        // The cursor always advances past every candidate examined on this call, including packages
        // skipped because this node is not a target: a non-targeted node must not rescan them
        // forever, and the advance can never jump over a package it still has to receive because
        // candidates are examined in published-at order.
        var nextCursor = page.Count == 0
            ? (cursor ?? "0")
            : (page[^1].PublishedAt ?? page[^1].CreatedAt).UtcTicks.ToString();

        var items = new List<SyncItem>(page.Count);
        var checksums = new Dictionary<string, string>(StringComparer.Ordinal);
        var deliveredPackageIds = new List<string>(page.Count);

        foreach (var package in page)
        {
            var targets = await dbContext.PublicationTargets
                .Where(x => x.PublicationPackageId == package.Id)
                .ToListAsync(cancellationToken);

            if (!IsDeliveredToNode(targets, normalizedNodeId, schoolIds))
            {
                continue;
            }

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
        var policyResult = SyncPushPolicy.Evaluate(item, existing?.Payload.RootElement);
        if (existing is not null || policyResult.Status is not "accepted")
        {
            return policyResult;
        }

        try
        {
            using var payload = JsonDocument.Parse(item.Payload.GetRawText());
            if (ContainsForbiddenNominalProperty(payload.RootElement))
            {
                return new PushItemResult(item.IdempotencyKey, "failed", "The payload contains a prohibited document or token field.");
            }

            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            if (dbContext.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL" &&
                item.EventType is SyncEventTypes.AttemptSubmitted)
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    $"SELECT pg_advisory_xact_lock({CentralStatsRollupService.AdvisoryLockNamespace}, {CentralStatsRollupService.AdvisoryLockKey});",
                    cancellationToken);
            }
            dbContext.SyncInbox.Add(new SyncInbox(
                Guid.NewGuid().ToString("N"),
                nodeId,
                item.EventType,
                item.AggregateType,
                item.AggregateId,
                item.IdempotencyKey,
                JsonDocument.Parse(item.Payload.GetRawText()),
                "processed",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow));

            if (item.EventType is SyncEventTypes.AttemptSubmitted)
            {
                await AddAttemptAsync(nodeId, item, payload.RootElement, cancellationToken);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            return new PushItemResult(item.IdempotencyKey, "accepted", null);
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

            return SyncPushPolicy.Evaluate(item, nowExisting.Payload.RootElement);
        }
        catch (JsonException exception)
        {
            dbContext.ChangeTracker.Clear();
            return new PushItemResult(item.IdempotencyKey, "failed", exception.Message);
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
            verifiedAt));

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
        if (examVersionRemoteId is not null)
        {
            await RecomputeGradeAsync(receivedAttemptId, examVersionRemoteId, receivedAnswers, cancellationToken);
        }
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
            nodeId);
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

    /// <summary>
    /// Independently recomputes the attempt grade from Central's own exam definition and adds a
    /// <see cref="CentralAttemptResult"/> to the same change-set as the attempt itself. An unknown
    /// <paramref name="examVersionRemoteId"/> is treated exactly like an absent one: no grade, no
    /// row, no failed push — Central never guesses a policy or fabricates a grade.
    /// </summary>
    private async Task RecomputeGradeAsync(
        string receivedAttemptId,
        string examVersionRemoteId,
        IReadOnlyList<ReceivedSubmissionAnswer> answers,
        CancellationToken cancellationToken)
    {
        var examVersion = await dbContext.ExamVersions
            .SingleOrDefaultAsync(x => x.Id == examVersionRemoteId, cancellationToken);
        if (examVersion is null)
        {
            return;
        }

        var blocks = await dbContext.ExamBlocks
            .Where(x => x.ExamVersionId == examVersion.Id)
            .OrderBy(x => x.OrderIndex)
            .ToListAsync(cancellationToken);
        var blockIds = blocks.Select(static x => x.Id).ToList();
        var answerKeys = await dbContext.AnswerKeys
            .Where(x => blockIds.Contains(x.ExamBlockId))
            .ToListAsync(cancellationToken);
        var answerKeyByBlockId = answerKeys.ToDictionary(static x => x.ExamBlockId);

        var gradableBlocks = new List<GradableBlock>(blocks.Count);
        foreach (var block in blocks)
        {
            var answerKey = answerKeyByBlockId.TryGetValue(block.Id, out var key) ? key : null;
            gradableBlocks.Add(GradingJsonMapper.MapBlock(
                block.Id,
                block.BlockType,
                answerKey?.ScoreValue,
                answerKey is null ? null : ToJsonElement(answerKey.CorrectAnswer)));
        }

        var blocksById = blocks.ToDictionary(static x => x.Id);
        var submitted = new Dictionary<string, SubmittedAnswer>();
        foreach (var received in answers)
        {
            if (!blocksById.TryGetValue(received.BlockId, out var block))
            {
                continue;
            }

            var mapped = GradingJsonMapper.MapSubmittedAnswer(block.BlockType, received.Answer.RootElement);
            if (mapped is not null)
            {
                submitted[received.BlockId] = mapped;
            }
        }

        var resolvedPolicy = await ResolveScoringPolicyAsync(examVersion.Id, examVersion.ScoringPolicy, cancellationToken);

        try
        {
            var result = new GradingEngine().Grade(new GradingExamVersion
            {
                ExamVersionId = examVersion.Id,
                DeclaredScoringPolicy = ScoringPolicyParser.Parse(resolvedPolicy),
                Blocks = gradableBlocks
            }, submitted);

            dbContext.CentralAttemptResults.Add(new CentralAttemptResult(
                Guid.NewGuid().ToString("N"),
                receivedAttemptId,
                result.GradingSchemaVersion,
                result.ScoringPolicy?.ToString(),
                "graded",
                result.Score,
                result.ScoreMax,
                JsonDocument.Parse(JsonSerializer.Serialize(result.Blocks, BlocksJsonOptions)),
                DateTimeOffset.UtcNow));

            await statsRollupService.UpsertForAttemptAsync(receivedAttemptId, result, examVersion.Id, cancellationToken);
        }
        catch (UngradableExamException)
        {
            dbContext.CentralAttemptResults.Add(new CentralAttemptResult(
                Guid.NewGuid().ToString("N"),
                receivedAttemptId,
                GradingSchemaVersion.Current,
                null,
                "ungradable",
                null,
                null,
                null,
                DateTimeOffset.UtcNow));
        }
    }

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
        var assets = await dbContext.ExamAssets
            .Where(x => x.ExamVersionId == version.Id)
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
            await ResolveScoringPolicyAsync(version.Id, version.ScoringPolicy, cancellationToken));
    }

    private async Task<string?> ResolveScoringPolicyAsync(string examVersionId, string? documentPolicy, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(documentPolicy))
        {
            return documentPolicy;
        }

        var assignment = await dbContext.GradingPolicyAssignments
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.ExamVersionId == examVersionId, cancellationToken);
        return assignment?.ScoringPolicy;
    }

    /// <summary>
    /// Resolves the set of school identifiers a node can match against a <c>school</c> publication
    /// target: the node's explicit SchoolId (if any), its enrolment CUE, and the internal School id
    /// for that CUE when a school row exists. Nodes enrol by CUE, so a school target may legitimately
    /// carry either a School id or a CUE.
    /// </summary>
    private async Task<HashSet<string>> ResolveNodeSchoolIdsAsync(string nodeId, CancellationToken cancellationToken)
    {
        var schoolIds = new HashSet<string>(StringComparer.Ordinal);
        var node = await dbContext.RegisteredNodes
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == nodeId, cancellationToken);
        if (node is null)
        {
            return schoolIds;
        }

        // Universal PCOPE keys are not attached to an individual school. Empty CUE is
        // the persisted marker for that scope and lets the node receive every publication.
        if (string.IsNullOrWhiteSpace(node.Cue))
        {
            var schools = await dbContext.Schools.AsNoTracking()
                .Select(static school => new { school.Id, school.Cue, school.Annex }).ToListAsync(cancellationToken);
            return schools.SelectMany(static school => new[]
                {
                    school.Id,
                    CueCode.TryFromSchool(school.Cue, school.Annex, out var canonicalCue)
                        ? canonicalCue
                        : school.Cue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    school.Cue.ToString(System.Globalization.CultureInfo.InvariantCulture)
                })
                .ToHashSet(StringComparer.Ordinal);
        }

        if (!string.IsNullOrWhiteSpace(node.SchoolId))
        {
            schoolIds.Add(node.SchoolId);
        }

        if (string.IsNullOrWhiteSpace(node.Cue))
        {
            return schoolIds;
        }

        schoolIds.Add(node.Cue);
        if (CueCode.TryNormalize(node.Cue, out var normalizedNodeCue))
        {
            var cue = long.Parse(normalizedNodeCue, System.Globalization.CultureInfo.InvariantCulture);
            var baseCue = long.Parse(normalizedNodeCue[..7], System.Globalization.CultureInfo.InvariantCulture);
            var annex = int.Parse(normalizedNodeCue[7..], System.Globalization.CultureInfo.InvariantCulture);
            var schools = await dbContext.Schools
                .AsNoTracking()
                .Where(x => x.Cue == cue || (x.Cue == baseCue && (x.Annex ?? 0) == annex))
                .Select(static school => new { school.Id, school.Cue, school.Annex })
                .ToListAsync(cancellationToken);
            foreach (var school in schools.Where(candidate =>
                         CueCode.TryFromSchool(candidate.Cue, candidate.Annex, out var normalized) && normalized == normalizedNodeCue))
            {
                if (!string.IsNullOrWhiteSpace(school.Id)) schoolIds.Add(school.Id);
            }
        }
        else if (long.TryParse(node.Cue, System.Globalization.NumberStyles.None,
                     System.Globalization.CultureInfo.InvariantCulture, out var legacyCue))
        {
            var legacySchoolId = await dbContext.Schools.AsNoTracking()
                .Where(school => school.Cue == legacyCue)
                .Select(static school => school.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(legacySchoolId)) schoolIds.Add(legacySchoolId);
        }

        return schoolIds;
    }

    /// <summary>
    /// A package with no <c>node</c>/<c>school</c> targets is delivered to every node, including an
    /// unknown nodeId. A package with at least one such target is delivered only to a node matching
    /// one of them. grade/subject/division targets describe the exam and never filter delivery.
    /// </summary>
    private static bool IsDeliveredToNode(IReadOnlyList<PublicationTarget> targets, string nodeId, HashSet<string> schoolIds)
    {
        var deliveryTargets = targets
            .Where(target => PublicationTargetTypes.DeliveryFilterTypes.Contains(target.TargetType, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (deliveryTargets.Count == 0)
        {
            return true;
        }

        foreach (var target in deliveryTargets)
        {
            if (string.Equals(target.TargetType, PublicationTargetTypes.Node, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(target.TargetId, nodeId, StringComparison.Ordinal))
            {
                return true;
            }

            if (string.Equals(target.TargetType, PublicationTargetTypes.School, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(target.TargetId) &&
                schoolIds.Contains(target.TargetId))
            {
                return true;
            }
        }

        return false;
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
