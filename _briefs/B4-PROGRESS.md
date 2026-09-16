# B4 · Local statistics and offline rollups — progress

## Status: CODE-COMPLETE for all 6 numbered tasks. Two acceptance criteria remain NOT
## VERIFIED for reasons stated below — do not read either as done.

This document was rewritten after an Orca runtime restart killed the leader that wrote PR #28
(merged as `7c8e819`, data layer only — task 1 and half of task 2/3). This session picked up
from that merge and closed tasks 2 (finish), 4, 5 and 6, plus exposed task 3's rebuild command
and added the missing ≥1,000-attempt endpoint-level test.

## Decision: rollup write stays OUTSIDE the submit transaction, made recoverable by a
## startup self-heal — recorded per the coordinator's explicit request on PR #29 review

**The question.** `AttemptEndpoints.cs`'s `/submit` handler calls
`IStatsRollupRepository.UpsertForAttemptAsync` after `SubmitWithOutboxAsync` returns `true`, i.e.
after that transaction has already committed. A crash between the two — realistic on a school
machine, 5400 rpm disk, mid-exam-day power loss — leaves an attempt marked `submitted`/`graded`
with no matching contribution in `stats_rollups`. Two ways to close that: (a) move the rollup
write inside the submit transaction, making divergence impossible; (b) keep it outside, accept
divergence can happen, make it recoverable and detectable.

**Chose (b).** Reasons:
- The rollup is explicitly a *derived* index in this design — the prior session's own reasoning
  (see "Re-grades are handled by rebuild, never by re-running the incremental upsert" further
  down this document) already treats it as reconstructible-by-design, not as a co-equal source of
  truth participating in the submission's atomicity boundary. Option (a) would quietly promote it
  to a hard dependency of the submit path, which nothing else in this plan asks for.
- Option (a) lengthens the write-lock hold on `/submit` — the single busiest write path on a
  single-writer SQLite file, on the machine that is, per the plan's own words about B5's sync
  service, "also delivering an exam" at the same time. Every extra millisecond under that lock is
  paid by the next student trying to submit. Option (b) keeps that hot path exactly as short as
  before this batch.
- The full-rebuild command (task 3) already exists, is reconciliation-tested to match the
  incremental path exactly, and was built for precisely this kind of recovery. Choosing (a) would
  make that capability redundant for its main purpose; choosing (b) uses it for what it is for.

**What (b) still needed, and what closes it.** Recoverability without detectability is not
enough — a school could show a supervisor an under-counted statistic with no signal anything is
wrong. Closed by `StatsRollupRepository.SelfHealIfInconsistentAsync()`, called once at
`LocalApiApplication` startup (`LocalApiApplication.cs`, in the same synchronous
migration/seeding block that already runs before the app accepts traffic): it compares a count of
graded, rollup-eligible attempts against `SUM(attempt_count)` in `stats_rollups` — using the exact
same eligibility filter as `RebuildAllAsync`'s tuple discovery (an earlier version of this check
used a looser filter and produced a **permanent false-positive on every boot** for any school that
had ever run a single non-nominal exam session; caught and fixed before merge, see the dispatch
notes below) — and calls `RebuildAllAsync()` only when they disagree. This closes the exact
failure window named above: a power cut forces a restart, and by the time the app is serving
requests again after that restart, the drift has already been found and repaired. No operator
action, no banner needed for the common case; a divergence is logged via
`ILogger<StatsRollupRepository>.LogWarning` when it does trigger a rebuild, so it is visible to
whoever reads Local API logs even though it self-heals silently from the operator's point of
view.

**Verified, not asserted:** `StatsRollupSelfHealTests.cs` proves both directions — a consistent
rollup is left untouched (`updated_at` unchanged, no spurious rebuild), and a rollup deliberately
never written (simulating the exact crash scenario: a graded `attempt_results` row with zero
matching `stats_rollups` rows) is fully recovered by one `SelfHealIfInconsistentAsync()` call, with
exact attempt/score counts.

## Per-task status (plan's own task numbering under "B4 · Local statistics and offline rollups")

| # | Task | Status | Detail |
|---|---|---|---|
| 1 | Migration `011_Stats.sql` (→ 013) | **DONE** (prior session) | `stats_rollups` + `stats_rollup_blocks`. |
| 2 | `StatsRollupService`: incremental update on attempt grading | **DONE** | `AttemptEndpoints.cs`'s `/submit` handler now calls `IStatsRollupRepository.UpsertForAttemptAsync` after a successful `SubmitWithOutboxAsync`, direct repository injection (no separate service class — KISS, one caller). Registered in DI. A submitted attempt updates its rollup today. |
| 3 | Full-rebuild command for recovery / post-re-grade reconciliation | **DONE** | Repository method existed already (prior session) and is reconciliation-tested. This session exposed it as `POST /api/stats/rebuild`, calling `RebuildAllAsync`. No auth, no request body — matches this codebase's existing endpoint style, none of which has auth today. |
| 4 | Local endpoints `GET /api/stats/school`, `/course`, `/exam` | **DONE** | `src/Local/PlanCope.Local.Api/Endpoints/StatsEndpoints.cs`. All three take `cue` (required), `schoolYear` (optional), `course` (optional, `/school` and `/exam` only). `/exam` includes the per-block breakdown (correct/partial/incorrect/blank/ungradable, always distinct). |
| 5 | Operator UI (per-course/per-exam breakdown, per-block difficulty, blank-vs-incorrect split) | **DONE** | `StatsWorkspace.tsx` in `PlanCope.Local.Host/ClientApp`, wired into `HostApp.tsx` behind a "Sesiones / Estadísticas" tab switcher (reused the already-defined-but-unused `.mode-tabs` CSS). Course table + per-exam `<details>` block tables. |
| 6 | CSV export | **DONE** | `GET /api/stats/export.csv?cue=&schoolYear=` — course-level CSV (`course,attempt_count,average_score_percent`), suppressed cells render `cohorte insuficiente`. **Decision: course-level only, deliberately, not a gap** — see below. |

### Decision: CSV export stays course-level, not per-block — owner-confirmed

The plan puts CSV export in B4 for one stated purpose: a school with no connectivity still has to
hand results to a supervisor. A supervisor needs course and exam figures, not per-block
difficulty — per-block breakdown is a teaching instrument for whoever wrote the exam, and that
person already has it, on-screen in `StatsWorkspace.tsx` and in the raw JSON from `/api/stats/exam`.
Adding it to the CSV would be building for a use case nobody has stated, which is exactly what
KISS forbids. If a supervisor turns out to need it in spreadsheet form, that is a present-tense
reason and the right time to add a second export route — not now, on spec. Recorded here so
whoever reads this next treats it as a decision to challenge on evidence, not a gap to "finish."

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
- **`ExamScoringPolicyPullTests.PullAsync_PersistsScoringPolicy_FromPublishedPackage` — RESOLVED,
  not flaky.** This session initially misjudged its intermittent failure as flakiness. Actual
  root cause, confirmed by the coordinator: it was order-dependent — `Dapper.DefaultTypeMap
  .MatchNamesWithUnderscores` was being set inside DI registration, so a repository constructed
  directly (bypassing DI, as some tests do) never triggered it. Fixed on `main` in `bc26da1` via
  a module initializer (`LocalDapperConfiguration.cs`) that sets it unconditionally at assembly
  load, before any repository can be constructed either way. Confirmed present after rebasing this
  branch onto current `main`. Not this batch's fix and not this batch's item — recorded here only
  so nobody re-opens it as a B4 concern.

## What's left for whoever picks this up next

1. **The 200ms and offline-unplugged criteria above** — hardware-gated, not code-gated.
2. **No auth on `/api/stats/*` or the new `/api/stats/rebuild`.** Matches every other Local
   endpoint in this codebase today (none of them have auth) — Local runs on a machine already
   unlocked by Phase A, so this is consistent, not a hole. If that changes, it is a Local-wide
   decision, not a B4-specific one.
3. ~~The plan's risk mitigation (periodic self-check + mismatch report) does not exist.~~ **CLOSED**
   this session: `SelfHealIfInconsistentAsync()` runs once at startup and self-heals via
   `RebuildAllAsync()` on drift — see the decision record above. It is a boot-time check, not a
   *periodic* one while the process stays running for days — if a school leaves the Local API
   running for an extended stretch without restarting, drift within that window is still only
   caught by an operator manually hitting `POST /api/stats/rebuild`. A true periodic in-process
   timer was judged unnecessary for now (adds a background timer to a single-purpose offline app
   for a window that closes on the next restart anyway) but is the next increment if this proves
   insufficient in practice.

CSV export scope (course-level only) is a recorded, owner-confirmed decision, not an open item —
see the decision record under task 6 above. `ExamScoringPolicyPullTests` is resolved on `main`
(`bc26da1`), not an open item either — see the NOT VERIFIED section above.

## Test plan (this batch, cumulative with the prior session's)

- [x] `dotnet build PlanCope.slnx -warnaserror` — 0 warnings, 0 errors (backend + host UI)
- [x] `dotnet test tests/PlanCope.Local.Api.Tests` — 64/65 pass (1 pre-existing skip from B2's
      `EnrolmentEndpointsTests.Redeem_endpoint_placeholder`, unrelated to this batch)
- [x] `dotnet test tests/PlanCope.Shared.Tests --filter FullyQualifiedName~CohortSuppression` — 14/14 pass
- [x] Endpoint-level aggregation test at 1,200 attempts, through the real HTTP endpoints,
      asserting exact totals per course/exam and exact block-level correct/incorrect counts
- [x] Self-heal test: a consistent rollup is left untouched by `SelfHealIfInconsistentAsync`; a
      rollup deliberately never written (simulated crash) is fully recovered by one call to it
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

Wave 4 (after coordinator review of PR #29): the startup self-heal (interface + repository +
`LocalApiApplication.cs` wiring, 3 files) landed clean on the first dispatch, but this leader's
own review of the diff — not a build failure — caught that the brief itself specified a
`gradedCountSql` missing the same eligibility filter `RebuildAllAsync` uses (school_year IS NOT
NULL, roster_section_id IS NOT NULL, blocks_json IS NOT NULL). Left as written, any school that
ever runs a single non-nominal exam session would see a permanent false-positive drift warning
and an unnecessary full rebuild on every single boot forever. Caught before running any test,
fixed with a second 1-file dispatch. Adding the required `ILogger<StatsRollupRepository>`
constructor parameter then broke 8 existing call sites across two test files (`StatsRollup
IncrementalTests.cs`, `StatsRollupReconciliationTests.cs`) that built the repository directly —
fixed with a third dispatch (`NullLogger<T>.Instance`, no new package). A fourth dispatch added
`StatsRollupSelfHealTests.cs`, proving the self-heal both leaves a consistent rollup alone and
recovers a deliberately-missing one with exact counts. Four dispatches for one decision — the
review-catches-a-bug-in-my-own-brief step is exactly why a leader reads every diff instead of
trusting a green build.
