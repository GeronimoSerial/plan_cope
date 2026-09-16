# B2 — Node enrolment and hardware identity: progress log

Level-2 leader log. Every wave dispatched via `opencode run` per
`scripts/LEVEL3-DISPATCH-PROTOCOL.md`; nothing here was written directly by the leader.

Read before designing: `PROJECT-CLOSURE-PLAN.md` §"### B2 ·", §2.2, §2.8, §6, §7, and B1's
merged contracts (`src/Shared/PlanCope.Shared.Contracts/Activation/ActivationContracts.cs`,
`ActivationAdminContracts.cs`) plus `_briefs/B1-PROGRESS.md`.

## Wave 1 — standalone infra (tasks 1 + schema prerequisite, fanned out)

Two disjoint slices, both pure infrastructure with no UI and no cross-slice file overlap:

- **Slice T1 — `HardwareFingerprintService`** (plan task 1). Files:
  `src/Local/PlanCope.Local.Api/Services/HardwareFingerprintService.cs`,
  `tests/PlanCope.Local.Api.Tests/HardwareFingerprintServiceTests.cs`.
- **Slice NID — `node_identity` schema + repository** (infra other tasks build on). Files:
  a new migration, `INodeIdentityRepository`/`NodeIdentityRepository`, an append-only edit to
  `LocalModels.cs`, tests.

First dispatch (`t1`+`nid` together): `t1` succeeded clean. `nid` failed with opencode's own
"database is locked" error against its session store — not a repo/SQLite issue, no files were
written. Re-dispatched `nid` alone (`nid2`); succeeded clean. Both reviewed independently,
verified by the leader (`dotnet build PlanCope.slnx -warnaserror` green, targeted
`dotnet test tests/PlanCope.Local.Api.Tests` green: 7/7 for T1, 3/3 for NID), then committed as
`feat(local): add hardware fingerprint composite service` and
`feat(local): add node_identity schema and repository`.

### Migration number collision — caught by the coordinator, not by this worktree

The leader's worktree branched from `65fd0b6` and could only see up to `008_Schools.sql`, so it
named its new migration `009_NodeIdentity.sql`. By the time it landed, `main` had already merged
B8's `009_PerformanceIndexes.sql`. The coordinator flagged this from outside the worktree (it
also warned B3's PR #24 will take `010`/`011` — not yet merged as of this rebase). Verified
independently before acting: `git fetch origin && git ls-tree -r origin/main --name-only | rg
"Migrations/0"` showed `009_PerformanceIndexes.sql` really is on `main`. Fixed by: rebasing onto
`origin/main`, then `git mv` (rename, not an in-place edit of an already-applied number) to
`010_NodeIdentity.sql`, rebuilt and reran the NID tests green post-rename, committed as
`fix(local): renumber node_identity migration to 010`, force-pushed (`--force-with-lease`) since
this branch had only been pushed minutes earlier with no other work based on it.
**Known follow-up risk, named rather than hidden**: if B3's PR #24 merges `010_ExamVersionScoringPolicy.sql`
before this branch does, `010_NodeIdentity.sql` will collide a second time and need another
rename to `012`. Checking migration numbers against a freshly fetched `main` immediately before
every future wave that touches `Data/Migrations/`, not just once here.

Also checked per the coordinator's request: `NodeIdentity`'s shape against B1's merged
`ActivationContracts.cs` (re-read on the rebased tree — unchanged from the pre-rebase read).
No mismatch — `FingerprintHash` (string) and eventual `NodeId` (string, filled after redeem)
line up field-for-field with `ActivationRedeemRequest.FingerprintHash` /
`ActivationRedeemResponse.NodeId`; `FingerprintComponentsJson` is stored as SQLite `TEXT` but
round-trips through the same JSON object shape `ActivationRedeemRequest.FingerprintComponents`
(a `JsonDocument`) expects. Access/refresh tokens are deliberately NOT in `node_identity` — they
belong in the existing generic `sync_state` key/value table (`central_url`/`node_id`/
`central_access_token` keys), per the plan and matching how `LocalExamPullService` etc. already
read credentials. `ActivationRedeemFailureReason.FingerprintCollision`'s doc comment ("identity
IS the (Cue, FingerprintHash) pair") also confirms the unique index on `node_identity.cue` is
the right constraint, not a unique index on `node_id`.

## Next

Wave 2 (Phase A rewrite + retire single-CUE build + `ActivationKeyStore.Load()` load-bearing,
and Phase B enrolment + credential refresh) not yet dispatched — gathering the remaining context
(frontend fetch conventions, Central's `ActivationController` route shapes, `Build-SchoolRelease.ps1`
retirement scope) before writing those briefs.
