using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlanCope.Central.Api.Data;
using PlanCope.Shared.Contracts.Exams;
using PlanCope.Shared.Domain.Central;

namespace PlanCope.Central.Api.Controllers;

/// <summary>
/// Admin surface for assigning a legacy scoring policy to already-published exam versions that
/// were published before the scoring-policy publish gate existed (or through a path that skipped
/// it). Listing surfaces only versions that genuinely need a policy — published, carrying at
/// least one multiple-choice block, no parseable policy on the document, and no assignment yet —
/// mirroring the exact rule <c>ExamsController.PublishVersion</c> enforces at publish time.
/// Bulk assignment audits who assigned what and when; it never silently overwrites an existing
/// assignment (re-assignment is a separate, single-item action).
/// </summary>
[ApiController]
[Authorize]
[Route("api/admin/grading-policies")]
public sealed class LegacyGradingPolicyController(PlanCopeDbContext dbContext) : ControllerBase
{
    [HttpGet("unassigned")]
    public async Task<ActionResult<IReadOnlyList<UnassignedExamVersionDto>>> ListUnassigned(
        CancellationToken cancellationToken = default)
    {
        var assignedVersionIds = dbContext.GradingPolicyAssignments
            .Select(static assignment => assignment.ExamVersionId);

        var candidates = await dbContext.ExamVersions
            .AsNoTracking()
            .Where(version => version.Status == "Published")
            .Where(version => dbContext.ExamBlocks.Any(block =>
                block.ExamVersionId == version.Id &&
                block.BlockType == PlanCope.Shared.Domain.BlockType.MultipleChoice))
            .Where(version => !assignedVersionIds.Contains(version.Id))
            .ToListAsync(cancellationToken);

        var withoutPolicy = candidates
            .Where(version => PlanCope.Shared.Grading.ScoringPolicyParser.Parse(version.ScoringPolicy) is null)
            .ToList();

        var examIds = withoutPolicy.Select(static version => version.ExamId).Distinct().ToList();
        var examsById = await dbContext.Exams
            .AsNoTracking()
            .Where(exam => examIds.Contains(exam.Id))
            .ToDictionaryAsync(static exam => exam.Id, cancellationToken);

        var result = withoutPolicy
            .Select(version => new UnassignedExamVersionDto(
                version.Id,
                examsById[version.ExamId].Code,
                version.VersionNumber,
                version.PublishedAt))
            .OrderBy(static dto => dto.ExamCode)
            .ThenBy(static dto => dto.VersionNumber)
            .ToList();

        return Ok(result);
    }

    [HttpPost("bulk-assign")]
    public async Task<ActionResult<BulkAssignGradingPolicyResult>> BulkAssign(
        [FromBody] BulkAssignGradingPolicyRequest? request,
        CancellationToken cancellationToken = default)
    {
        if (request is null ||
            request.ExamVersionIds.Count == 0 ||
            PlanCope.Shared.Grading.ScoringPolicyParser.Parse(request.ScoringPolicy) is null)
        {
            return BadRequest("A valid scoring policy and at least one exam version are required.");
        }

        var callerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(callerId))
        {
            return Unauthorized();
        }

        var distinctIds = request.ExamVersionIds.Distinct().ToList();
        var existingAssignments = await dbContext.GradingPolicyAssignments
            .Where(assignment => distinctIds.Contains(assignment.ExamVersionId))
            .Select(static assignment => assignment.ExamVersionId)
            .ToListAsync(cancellationToken);

        var assignedAt = DateTimeOffset.UtcNow;
        var assignedCount = 0;
        var rejectedIds = new List<string>();

        foreach (var examVersionId in distinctIds)
        {
            if (existingAssignments.Contains(examVersionId))
            {
                rejectedIds.Add(examVersionId);
                continue;
            }

            dbContext.GradingPolicyAssignments.Add(new GradingPolicyAssignment(
                Guid.NewGuid().ToString("N"),
                examVersionId,
                request.ScoringPolicy,
                callerId,
                assignedAt,
                request.Note));
            assignedCount++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new BulkAssignGradingPolicyResult(assignedCount, rejectedIds));
    }
}