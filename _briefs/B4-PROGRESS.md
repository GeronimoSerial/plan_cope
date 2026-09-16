# B4 · Local statistics and offline rollups — progress

## Status: PARTIAL — data layer only. Do not read as "done" or even "half done" in the
## user-facing sense: nothing a school operator can see or touch exists yet.

Of the plan's 6 main tasks, only the storage/rollup core (roughly task 1 and the compute half
of task 2/3) is built and tested. Nothing is wired into the running application, and tasks 4–6
(endpoints, operator UI, CSV export) were never started. Owner instruction mid-batch was to wind
down after the in-flight slice — this document reflects exactly where that left things.

## Two corrections applied from the leader mandate

1. **Migration number.** Used `013_Stats.sql`, not the `011_Stats.sql` the plan text names —
   verified against `ls src/Local/PlanCope.Local.Api/Data/Migrations/` at start, which showed
   009–011 already taken (008 Schools, 009 PerformanceIndexes, 010 ExamVersionScoringPolicy, 011
   Grading) and 012 reserved by an in-flight PR.
2. **200ms rollup criterion.** Never measured, never faked. See "NOT VERIFIED" section below.

## Per-task status (plan's own task numbering under "B4 · Local statistics and offline rollups")

| # | Task | Status | Detail |
|---|---|---|---|
| 1 | Migration `011_Stats.sql` (→ 013) | **DONE** | `stats_rollups` + `stats_rollup_blocks` tables, keyed by `(cue, school_year, course, exam_version_id)`. |
| 2 | `StatsRollupService`: incremental update on attempt grading | **PARTIAL** | The incremental repository method (`StatsRollupRepository.UpsertForAttemptAsync`) exists, is O(1) in the number of existing attempts, and is unit-tested. **There is no `StatsRollupService`, and nothing calls this method from anywhere in the running application** — grep confirms zero references to `StatsRollup` in `LocalDataServiceCollectionExtensions.cs`, `AttemptEndpoints.cs`, or `LocalApiApplication.cs`. A submitted attempt today does **not** update any rollup. |
| 3 | Full-rebuild command for recovery / post-re-grade reconciliation | **DONE** (as a repository method) | `RebuildTupleAsync` / `RebuildAllAsync` implemented and proven against the incremental path by a reconciliation test, including a re-grade scenario. **Not exposed anywhere** — no CLI, no admin endpoint, no scheduled job. It only exists as a method callable from a test today. |
| 4 | Local endpoints `GET /api/stats/school`, `/course`, `/exam` | **NOT STARTED** | No file exists. |
| 5 | Operator UI (per-course/per-exam breakdown, per-block difficulty, blank-vs-incorrect) | **NOT STARTED** | No component exists in `PlanCope.Local.Host/ClientApp`. |
| 6 | CSV export | **NOT STARTED** | No endpoint, no UI trigger. |

**Cohort suppression filter** (named in the plan's risk section, not the numbered task list, but
called out in the leader mandate as B4's single-source point): **DONE**.
`PlanCope.Shared.Domain.CohortSuppression` — minimum cohort 5, `"school"` scope never suppressed
at any count, any other scope value suppressed below 5, suppressed cells expose a fixed
`"cohorte insuficiente"` label rather than a numeric zero or a blank (a zero would misread as
"nobody passed", which is the specific harm the gate names). Placed in the existing
`PlanCope.Shared.Domain` project — not a new project — specifically so a later, separate batch
building Central's province-scoped `/api/stats/*` view can reuse this exact type without
duplicating the rule. It is fully unit-tested but **not used by anything yet**: Local never
serves a non-`"school"` scope, so in this codebase today the filter is inert, dormant
infrastructure waiting for a caller.

## Why these design choices (the WHY the diff doesn't show)

- **The tuple is 4 columns, not 3.** `(cue, school_year, course, exam_version_id)` — school_year
  is in the key because the same course/exam can recur across years and a rollup must not blend
  them; a school looking at "this year's" figures would otherwise see prior years mixed in
  silently.
- **`course` is never NULL in `stats_rollups`, even though `local_roster_sections.course` can be
  NULL.** A NULL course value would make the tuple's uniqueness constraint behave unpredictably
  in SQLite (NULLs don't collide under UNIQUE) and would let two "unassigned course" rollups
  silently duplicate instead of merging. The sentinel `"sin_asignar"` fixes both problems, but
  it means the SQL that reads existing data must normalize the same way — this exact
  inconsistency (normalized when writing, raw when reading) was the reconciliation-test bug
  described below.
- **Re-grades are handled by rebuild, never by re-running the incremental upsert.** An
  attempt's `grading_schema_version` only increases on a re-grade; `UpsertForAttemptAsync` is
  deliberately not idempotent against being called twice for the same attempt, because in the
  real submit flow it never legitimately is. Re-grade reconciliation is `RebuildTupleAsync`'s
  job precisely because it re-derives from the CURRENT (`MAX(grading_schema_version)`)
  `attempt_results` row, which naturally supersedes the old one.
- **A real bug the reconciliation test caught, and why it matters beyond this one fix.** The
  first implementation of `RebuildAllAsync`/`RebuildTupleAsync` silently dropped every rollup
  whose course was unassigned: the tuple-discovery query normalized NULL/empty course to the
  `"sin_asignar"` sentinel before calling the per-tuple rebuild, but the per-tuple rebuild's own
  SQL compared against the *raw*, non-normalized roster course — so the comparison never matched,
  the existing rollup row got deleted, and nothing was reinserted. This shipped from the level-3
  dispatch with a passing build and a green non-reconciliation test suite; only the exact-equality
  reconciliation test caught it. This is the direct, concrete argument for why "rollups match a
  full rebuild exactly, verified by a reconciliation test" is in the plan as a hard acceptance
  criterion rather than a nice-to-have: without it, this defect would have shipped silently, and
  every province-level query for an unassigned-course cohort (once B6 builds on this) would have
  returned nothing, with no error anywhere.

## NOT VERIFIED — named explicitly, per the leader mandate

- **"A rollup query returns under 200ms on the low-end target profile."** Cannot be verified in
  this environment. That profile is a 2-core/4GB RAM/HDD Windows machine; this environment is
  Linux, and the win-x64 host (`PlanCope.Local.Host`) cannot even build here (confirmed as an
  existing, pre-batch limitation — see B8's PR #20, which hit the identical wall for its own
  low-end-hardware criteria). No timing test was written against this criterion, and none should
  be trusted if one appears later without naming the hardware it ran on. **What's needed to
  close it:** the reference-profile machine described in `docs/reference-profile.md` (added by
  B8), running the query added under task 4 once it exists — which it does not yet.
- **Offline gate (§7 "Offline").** Not verified, because it cannot be: the endpoints that would
  let an operator see a statistic don't exist yet (task 4/5 not started). The gate that matters
  most for this batch — "every statistic computes and displays with the network cable
  unplugged" — has nothing to display yet.
- **"Aggregation by course, by year and by CUE each return correct figures on a seeded fixture of
  ≥1,000 attempts."** Partially exercised: the reconciliation test seeds attempts across multiple
  courses/years/exam versions and asserts exact rollup correctness, but at 50 attempts, not
  ≥1,000, and only against the storage layer directly — no endpoint exists to query through.
- **CI, not just local.** Everything above was run and is green on this machine only
  (`dotnet build PlanCope.slnx -warnaserror`: 0 warnings, 0 errors; `dotnet test
  tests/PlanCope.Local.Api.Tests`: 40/40 pass, up from 37 before this batch;
  `tests/PlanCope.Shared.Tests` cohort-suppression tests: 14/14 pass). A green local suite is not
  a pass — this has been pushed for CI to judge independently, not asserted as done on the
  strength of the local run.
- **One known, pre-existing CI-only failure, not from this batch:**
  `PlanCope.Local.Api.Tests.ExamScoringPolicyPullTests.PullAsync_PersistsScoringPolicy_FromPublishedPackage`
  passes on Linux (confirmed here) and is reported to fail on `windows-latest` CI with a Dapper
  `SyncState` materialization error. It is on `main`, predates this batch, and is out of scope
  here per owner instruction — if CI on this PR shows it red, that is why.

## What's needed to finish this batch (for whoever picks it up next)

In dependency order:
1. **Wire the incremental path**: a thin `StatsRollupService` (or direct `IStatsRollupRepository`
   injection) called from `AttemptEndpoints.cs` after `SubmitWithOutboxAsync` succeeds, plus DI
   registration in `LocalDataServiceCollectionExtensions.cs`. Without this, nothing populates
   `stats_rollups` in the running app no matter how correct the storage layer is.
2. **The three GET endpoints** (task 4) plus CSV export (task 6) — new file(s) under
   `src/Local/PlanCope.Local.Api/Endpoints/`, registered in `LocalApiApplication.cs`.
3. **The operator UI** (task 5) in `PlanCope.Local.Host/ClientApp` — a new stats workspace,
   wired into `HostApp.tsx` alongside the existing `SessionsWorkspace`.
4. Only once (2) exists: the ≥1,000-attempt aggregation-correctness test and the (necessarily
   still-unverifiable-here) 200ms timing check, written against the real endpoint rather than
   the repository directly.
5. Expose `RebuildAllAsync`/`RebuildTupleAsync` somewhere an operator or an automated self-check
   can actually trigger it — right now it is dead code from the running application's point of
   view, reachable only from tests. The plan's risk mitigation ("a periodic self-check that
   compares a sampled tuple and reports mismatch") is not built.

## Test plan (this batch only)

- [x] `dotnet build PlanCope.slnx -warnaserror` — 0 warnings, 0 errors
- [x] `dotnet test tests/PlanCope.Local.Api.Tests` — 40/40 pass (37 pre-existing + 3 new files'
      worth: incremental, reconciliation)
- [x] `dotnet test tests/PlanCope.Shared.Tests --filter FullyQualifiedName~CohortSuppressionTests`
      — 14/14 pass
- [ ] Endpoint-level tests — none exist, no endpoint exists
- [ ] UI tests — none exist, no UI exists
- [ ] 1,000+ attempt performance measurement — not run
- [ ] 200ms low-end-hardware criterion — cannot be run in this environment, named above

## Dispatch notes (for whoever reads the scratch dispatch logs)

The first dispatch (`core-brief.txt`, 23 files named in one brief) died at `rc=124` after 420s
having written nothing — it spent its entire budget re-reading context already known, never
reached writing. Corrected by splitting into narrow, mostly 1–3-file slices with facts inlined
directly rather than pointed at via "go read X." Every slice after that split landed real,
buildable, tested code, including a follow-up slice that fixed a real bug the reconciliation test
caught (see above). Full detail in the scratch dispatch directory's logs if needed; nothing there
is required reading to pick this batch back up — this document and the code are the whole story.
