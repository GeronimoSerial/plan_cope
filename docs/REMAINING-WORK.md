# Remaining work

State at the close of the 2026-09-15 delegation session. Written so someone with none of this
session's context can pick it up.

## Merged

| Batch | Merge | Status |
|---|---|---|
| B0 · CUE identity, test scaffolds | `898da63` | Closed except the production-snapshot data check |
| B1 · Activation keys (Central) | `65fd0b6` | Closed |
| B8 · Performance | `e4ffb38` | **Code-complete, NOT verified** — four criteria need real hardware |
| B3 · Grading engine | `e7d1d59` | Closed |

## In flight at session close

**B2 · Node enrolment** — worktree `b2-node-enrolment`, Local migration **012**.
Hardware fingerprint (composite hash plus which components matched, so drift is diagnosable),
`node_identity` schema and repository, Phase A backend and Phase B enrolment committed.
Instructed to finish its in-flight slice, commit, push and open a PR. Not started: the
re-activation screen for a locked node, and the credential-refresh 401 handling across the
three sync services.

**B4 · Local statistics** — worktree `b4-local-stats`, Local migration **013**.
`013_Stats.sql`, `CohortSuppression`, `StatsRollupRepository` with the incremental
tuple-scoped recompute, and the suppression test. Not started: the full-rebuild command and
its reconciliation test, the four stats endpoints, the operator UI, CSV export.

Both were told to leave their `_briefs/B<n>-PROGRESS.md` self-sufficient. **Read those first**
— they carry the reasoning that is not in the diffs.

## Not started

| Batch | Blocked on | Note |
|---|---|---|
| B5 · Autonomous synchronisation | B2 | Owns revocation **detection** — B2 only executes enforcement |
| B7 · Gated updates, integrity, rollback | B2 | Independent of the grading/stats track |
| B6 · Central administration surfaces | B3 ✓ and B4 | |
| B9 · Fail-path UX and E2E hardening | B5, B7, B8 ✓ | Closing batch; carries the `LoginResponse` task added this session |

The shape of the rest is three waves, not four loose batches: **B2 and B4 → B5, B7 and B6 →
B9 alone.** The critical path runs through B2, which blocks two of the four.

---

## Open defects

### RESOLVED — the "flaky Windows test" was order-dependent, and it is fixed

`ExamScoringPolicyPullTests.PullAsync_PersistsScoringPolicy_FromPublishedPackage` failed
intermittently on `windows-latest` with a `SyncState` materialization error. Fixed in `bc26da1`.

**It was never about Windows, and it was never flaky.** Dapper's
`MatchNamesWithUnderscores` is process-wide static state, but it was being set inside
`AddPlanCopeLocalData` — so it only took effect once DI had run. A repository constructed
directly, in a test or a tool or a background service, never triggered it. The test therefore
**passed when another test had already built the container and failed when it ran first**; the
Windows runner simply ordered it differently. Moving the configuration to a module initializer
makes it apply before any code can observe it.

Proven in both directions by running that single test in isolation: it fails on the old `main`
and passes with the fix.

**This entry is kept as a record of how it was got wrong twice.** It was first written here as
a live defect on `main` — wrong, because main was green. It was then rewritten as "flaky, do
not chase until it recurs" — also wrong, because intermittent is not the same as random, and
the recurrence condition fired within the hour when it blocked a batch PR. **An intermittent
failure with an unexplained mechanism is an unexplained failure, not a tolerable one**; the
honest position while the mechanism was unknown was "cause unknown", not "flaky".

---

## Cannot be closed by any agent in this environment

These are owner tasks. They are not incomplete work; they need access or hardware that does
not exist here.

1. **The production-snapshot data check (B0 task 1).** Migrations are proven reversible
   against a real Postgres 17 in CI — the `central-migrations` job applies every migration
   from empty and rehearses the rollback. But **an empty database has no duplicate CUEs to
   find.** Whether production data survives `core.schools.Cue` becoming unique needs a
   restored snapshot.

2. **B8's four performance criteria.** Cold start, 100-question render, idle CPU and the real
   Argon2id timing each need a **2-core / 4 GB / HDD Windows machine**. The figures currently
   recorded came from a 16-core development box and are labelled as such.

3. **The win-x64 Host cannot build in this environment at all.** WinForms + WebView2 does not
   compile on Linux, so R2R, trimming, DPAPI and hardware fingerprinting are unverified **by
   construction, not by omission**. This constrains B2, B5, B7 and B9 — not only B8.

4. **Activation key distribution.** Keys are universal bearer secrets. How they reach 1,440
   schools without circulating in a group chat is the question this plan cannot answer, and
   `max_activations` is the only technical control limiting the damage if they do.

5. **Shipping unsigned.** Recorded in the plan as an accepted risk rather than a solved
   problem: it normalises clicking past a security warning on machines holding data for
   227,598 minors.

---

## Housekeeping

- **PR #25** (three structural CI guards) is blocked on the Windows test failure above, not on
  anything wrong with the guards.
- **PR #9**, opened 2026-09-09, is `DIRTY` and adds a `ci-cd-velopack-coolify-plan.md` that
  already exists at the repo root plus a batch plan that `PROJECT-CLOSURE-PLAN.md` supersedes.
  It looks obsolete — **closing it is the owner's call, not the coordinator's.**
- Plan drift recorded in `PROJECT-CLOSURE-PLAN.md` rather than left to contradict the code:
  B2's Local migration `009_NodeIdentity.sql` was renamed twice — first to `010_NodeIdentity.sql`
  (after B8's `009_PerformanceIndexes.sql` merged first), then to `012_NodeIdentity.sql` (after
  B3's `010_ExamVersionScoringPolicy.sql`/`011_Grading.sql` merged as an open PR) — both
  collisions caught and resolved before merge, not after (`_briefs/B2-PROGRESS.md`). B3's
  `010_Grading.sql` shipped as `011`, and B4's `011_Stats.sql` as `013`, because the Local
  migration sequence collided four times between parallel branches.

## How this was run

`docs/DELEGATION-RULES.md` holds the fourteen rules this session established, each with the
failure that produced it. `scripts/LEVEL3-DISPATCH-PROTOCOL.md` holds the dispatch mechanics.
`BATCH-EXECUTION-STATUS.md` is the full review log, cycle by cycle, including the corrections
that turned out to be wrong.
