# B4 · Local statistics and offline rollups — progress

## Status: CODE-COMPLETE for all 6 numbered tasks. Two acceptance criteria remain NOT
## VERIFIED for reasons stated below — do not read either as done.

This document was rewritten after an Orca runtime restart killed the leader that wrote PR #28
(merged as `7c8e819`, data layer only — task 1 and half of task 2/3). This session picked up
from that merge and closed tasks 2 (finish), 4, 5 and 6, plus exposed task 3's rebuild command
and added the missing ≥1,000-attempt endpoint-level test.

## Per-task status (plan's own task numbering under "B4 · Local statistics and offline rollups")

| # | Task | Status | Detail |
|---|---|---|---|
| 1 | Migration `011_Stats.sql` (→ 013) | **DONE** (prior session) | `stats_rollups` + `stats_rollup_blocks`. |
| 2 | `StatsRollupService`: incremental update on attempt grading | **DONE** | `AttemptEndpoints.cs`'s `/submit` handler now calls `IStatsRollupRepository.UpsertForAttemptAsync` after a successful `SubmitWithOutboxAsync`, direct repository injection (no separate service class — KISS, one caller). Registered in DI. A submitted attempt updates its rollup today. |
| 3 | Full-rebuild command for recovery / post-re-grade reconciliation | **DONE** | Repository method existed already (prior session) and is reconciliation-tested. This session exposed it as `POST /api/stats/rebuild`, calling `RebuildAllAsync`. No auth, no request body — matches this codebase's existing endpoint style, none of which has auth today. |
| 4 | Local endpoints `GET /api/stats/school`, `/course`, `/exam` | **DONE** | `src/Local/PlanCope.Local.Api/Endpoints/StatsEndpoints.cs`. All three take `cue` (required), `schoolYear` (optional), `course` (optional, `/school` and `/exam` only). `/exam` includes the per-block breakdown (correct/partial/incorrect/blank/ungradable, always distinct). |
| 5 | Operator UI (per-course/per-exam breakdown, per-block difficulty, blank-vs-incorrect split) | **DONE** | `StatsWorkspace.tsx` in `PlanCope.Local.Host/ClientApp`, wired into `HostApp.tsx` behind a "Sesiones / Estadísticas" tab switcher (reused the already-defined-but-unused `.mode-tabs` CSS). Course table + per-exam `<details>` block tables. |
| 6 | CSV export | **DONE** | `GET /api/stats/export.csv?cue=&schoolYear=` — course-level CSV (`course,attempt_count,average_score_percent`), suppressed cells render `cohorte insuficiente`. Scope note: this is course-level only, not the per-block breakdown — task 6's stated purpose is "hand results to a supervisor," which the course-level summary satisfies; the block-level detail stays in the JSON/UI surface. If a future batch needs block-level CSV, this is where to add it. |

**Cohort suppression** (unchanged from prior session, now actually wired to real callers):
`PlanCope.Shared.Domain.CohortSuppression` / `SuppressibleValue<T>`. Every stats endpoint calls
into `StatsQueryRepository` with the literal constant `"school"` as `rosterScope` — Local is a
single school's offline machine, it never has another scope. One filter, parameterised by
`roster_scope`, not two aggregation paths — Central's future province-scoped view reuses this
exact repository/DTO shape with a different `rosterScope` value.

## A real bug this session's own test caught (name it plainly, per the mandate)

`StatsQueryRepository`'s three query methods all do `SUM(attempt_count) AS AttemptCount` and
mapped the result into private Dapper records declaring `int AttemptCount`. SQLite's `SUM()`
over an INTEGER column returns a 64-bit value; Dapper's constructor-matching materialization
requires an exact type match and does not narrow `long` to `int`. Every one of the three GET
endpoints (`/school`, `/course`, `/exam`) threw `InvalidOperationException` on every real call —
this shipped from an earlier wave in this same session with a green `dotnet build` (it is a
runtime-only failure, not a compile error) and would have gone undetected without an
endpoint-level test that actually exercises the SQL. The ≥1,000-attempt aggregation test
(`StatsEndpointsAggregationTests.cs`, 1,200 seeded attempts) caught it on its first run,
immediately after being written. Fixed by widening the affected private records
(`RollupTotalsRow`, `CourseStatsRow`, `ExamStatsRow`, and — caught by the same fix pass —
`BlockStatRow`, which has the identical bug for the per-block `SUM()` counts) to `long`, casting
to `int` only at the DTO-construction boundary. This is the same failure *shape* as the
pre-existing `SyncState` Dapper materialization bug noted below: **a green build proves nothing
about a runtime type-materialization mismatch; only a test that runs the actual query does.**

## NOT VERIFIED — named explicitly, per the leader mandate

- **"A rollup query returns under 200ms on the low-end target profile."** Still cannot be
  verified in this environment (Linux; the reference profile is a 2-core/4GB/HDD Windows
  machine, and `PlanCope.Local.Host` — win-x64 — does not build here). The endpoints now exist,
  so this criterion is no longer blocked by "nothing to measure" — it is blocked purely on
  hardware access. What's needed to close it: run `GET /api/stats/school` (and `/course`,
  `/exam`) against the reference-profile machine (`docs/reference-profile.md`, from B8) with a
  realistic rollup table size.
- **Offline gate, cable-unplugged sense.** Not physically tested with networking disabled. Argued
  by construction instead: `StatsQueryRepository` and `StatsRollupRepository` only ever open
  `ILocalSqliteConnectionFactory` connections (local SQLite file), and `StatsEndpoints.cs` has no
  `HttpClient`/outbound call anywhere in the request path. The operator UI (`StatsWorkspace.tsx`)
  only calls this same-machine Local API via `apiBaseUrl` (`http://127.0.0.1:...`), never a
  remote host. This is a structural argument, not a measurement — if this needs to be an
  affirmative "yes, tested," it still has to be pulled with the network cable out, once, on a
  build of `PlanCope.Local.Host`.
- **CI, not just local.** Everything above is green on this machine only:
  `dotnet build PlanCope.slnx -warnaserror`: 0 warnings/errors (includes the `tsc && vite build`
  step for the host UI); `dotnet test tests/PlanCope.Local.Api.Tests`: 41/41 pass (up from 40).
  Pushed for CI to judge independently.
- **The pre-existing CI-only failure named by the prior session**
  (`ExamScoringPolicyPullTests.PullAsync_PersistsScoringPolicy_FromPublishedPackage`, a Dapper
  `SyncState` materialization error) **is no longer reproducing on Linux either, in this
  session's runs** — it failed once early in this session, then passed on every subsequent run
  with no code change to that path. Contradicts the prior note that it "passes on Linux
  (confirmed here)." Treat it as flaky rather than deterministic on either OS until someone
  investigates further; it is unrelated to any file this session touched (`SyncStateRepository`/
  `LocalExamPullService`, nowhere near stats).

## What's left for whoever picks this up next

1. **The 200ms and offline-unplugged criteria above** — hardware-gated, not code-gated.
2. **CSV export is course-level only.** If an operator needs the per-block blank/incorrect
   breakdown offline in spreadsheet form (not just on-screen), add a second export route.
3. **No auth on `/api/stats/*` or the new `/api/stats/rebuild`.** Matches every other Local
   endpoint in this codebase today (none of them have auth) — flagging in case that changes.
4. **The plan's risk mitigation** ("a periodic self-check that compares a sampled tuple and
   reports mismatch") still does not exist. `POST /api/stats/rebuild` gives an operator a manual
   lever; nothing calls it automatically.
5. Investigate the flaky `ExamScoringPolicyPullTests` test noted above — out of this batch's
   scope, but worth a ticket.

## Test plan (this batch, cumulative with the prior session's)

- [x] `dotnet build PlanCope.slnx -warnaserror` — 0 warnings, 0 errors (backend + host UI)
- [x] `dotnet test tests/PlanCope.Local.Api.Tests` — 41/41 pass
- [x] `dotnet test tests/PlanCope.Shared.Tests --filter FullyQualifiedName~CohortSuppression` — 14/14 pass
- [x] Endpoint-level aggregation test at 1,200 attempts, through the real HTTP endpoints,
      asserting exact totals per course/exam and exact block-level correct/incorrect counts
- [ ] 1,000+ attempt performance measurement (needs the reference-profile machine)
- [ ] 200ms low-end-hardware criterion (needs the reference-profile machine)
- [ ] Offline test with networking physically disabled (argued by construction, not measured)

## Dispatch notes (level-3 waves, this session)

Three waves, all level-3 dispatched per the protocol (this leader wrote no production code).
Wave 1: wire incremental call (2 files) + new query repository (2 files), dispatched
concurrently — the first attempt at the wire slice hit `rc=1` ("database is locked", opencode's
own shared session DB contention, not a brief problem) and was retried alone successfully. A
follow-up 1-file dispatch fixed a `SUM()`-into-non-nullable-int NULL crash in the school-stats
query (no matching rows → SQL NULL). Wave 2: stats GET+CSV endpoints (3 files) and the operator
UI (3 files) dispatched concurrently, fully disjoint (backend `.cs` vs frontend `.tsx`) — both
landed clean on the first attempt. Wave 3: the ≥1,000-attempt aggregation test and the
rebuild-exposure endpoint dispatched concurrently (disjoint: test project vs. a single existing
endpoints file) — the rebuild endpoint landed clean; the aggregation test dispatch hit `rc=124`
(timeout) but had already written a complete, correct test file plus a diagnostic scratch file
(`TempDapperProbe.cs`, deleted before commit) that had already isolated the `SUM()`/`int`
materialization bug described above. That bug was fixed in one more 1-file dispatch. Every
dispatch stayed within the 1-3-file budget the prior session's numbers established; none needed
a second attempt for being too broad.
