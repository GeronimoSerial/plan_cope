# B6 · Central administration surfaces — progress

## Status: MERGED as `4dcf30a`. All 10 numbered tasks closed. This document is a handoff note
## for B9 (full-system E2E + documentation reconciliation) — not further B6 work.

Ten tasks landed across activation key management, node/device registry, `/api/stats/*` scoped
per D13, the builder's scoring-policy gate, Text/Image block authoring, and legacy scoring-policy
assignment. Full detail is in the merged commit history on `feat/b6-central-admin` (16 commits,
each one story) and the coordinator's own review notes on PR #39.

## Handoff to B9 — facts its E2E scenario would otherwise have to rediscover

**Central's stats rollup silently skips an attempt with no roster linkage.** Unlike Local,
Central has no direct `school_year`/`course` columns on the delivery session — it resolves them
by joining `ReceivedStudentAttempt.RosterSectionId → GeRosterSection → GeRosterSnapshot`
(`CentralStatsRollupService.UpsertForAttemptAsync`,
`src/Central/PlanCope.Central.Api/Services/CentralStatsRollupService.cs`). If a synced attempt has
no `RosterSectionId`, or its delivery session has no `SchoolId`, or the roster snapshot has no
`SchoolYear`, the method returns without writing a row — no exception, no log, nothing. If B9's
E2E scenario pushes a graded attempt and then expects `/api/stats/*` to show it, and the roster
snapshot/section chain wasn't seeded on Central first, the numbers will just be zero with no error
to explain why. This is deliberate (an attempt that can't be attributed to a cohort shouldn't be
counted), but it is exactly the kind of silent gap a full-system scenario needs to know about
before it goes looking for a bug that isn't one.

**`/api/stats/*` responses mix numbers and the literal string `"cohorte insuficiente"`.**
`attemptCount`/`averageScorePercent` render as that string instead of a number whenever the
cohort is below 5 AND the caller is province-scoped (never for school scope, which always sees
its own real numbers — reused from B4's `CohortSuppression`, untouched by this batch). Any E2E
assertion reading these fields needs to either seed ≥5 attempts for a province-scope check, or
use a school-scope token, or it will get a string where it expected a number.

**Image blocks in the builder are author-by-pasted-asset-id, not upload.** The backend fully
supports `BlockType.Image` (already did, before this batch); the builder can now author one, but
only by typing in an already-uploaded asset's id — there is no file picker yet. If B9's scenario
walks the builder UI expecting to attach an image file directly, that path doesn't exist. Wiring
real upload (`POST exams/versions/{id}/assets`) through the question-editor tree is a named,
deliberate follow-up from B6, not something dropped by accident.

**Legacy policy assignment only ever lists what actually needs one.** `GET
api/admin/grading-policies/unassigned` filters to published versions that (a) have at least one
`MultipleChoice` block, (b) have no parseable document policy, and (c) have no assignment row yet
— exactly mirroring the publish-time gate's own rule. A published exam with only `TrueFalse`/
`ShortAnswer`/`Text`/`Image` blocks will never appear there, by design, because it was never
blocked from publishing without a policy either. If B9's E2E fixture data includes such an exam
and expects to see it on `/politicas-legado`, it won't — that's correct behavior, not a bug to
chase.

**`central-migrations` CI (real Postgres 17, apply + rollback) was not independently verified in
this environment.** Two new migrations landed this batch (`AddStatsRollups`,
`AddGradingPolicyAssignments`) — both build clean and were generated via `dotnet ef migrations
add`, but I had no live Postgres instance here to run `dotnet ef database update` / rollback
rehearsal against. If B9's reconciliation touches CI history, that job's result on PR #39 is the
first real confirmation either migration's `Down()` actually reverses cleanly.
