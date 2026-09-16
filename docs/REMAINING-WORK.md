# Remaining work

State at the close of the delegation session, **2026-09-16**. The ten-batch plan in
`PROJECT-CLOSURE-PLAN.md` is complete: every batch is merged to `main`. Written so someone
with none of this session's context can pick up what is left.

## The plan is closed

| Batch | Merge | Status |
|---|---|---|
| B0 · CUE identity, test scaffolds | `898da63` | Closed except the production-snapshot data check (owner) |
| B1 · Activation keys (Central) | `65fd0b6` | Closed |
| B2 · Node enrolment | `58b63ae` | Closed |
| B3 · Grading engine | `e7d1d59` | Closed |
| B4 · Local statistics | `6b45c0e` | Closed |
| B5 · Autonomous synchronisation | `c4e993b` | Closed |
| B6 · Central administration surfaces | `4dcf30a` | Closed |
| B7 · Gated updates, integrity, rollback | `dc00cb5` | Closed except what needs a real Windows client (owner) |
| B8 · Performance | `e4ffb38` | **Code-complete, NOT verified** — four criteria need real hardware (owner) |
| B9 · Fail-path UX and E2E hardening | `83ab3f2` | Closed; carries the final PROVEN / NOT PROVEN table |

`_briefs/B9-PROGRESS.md` holds the project's last word on **what is actually proven**. It
separates three things that are easy to collapse into one and must not be: a claim
**exercised** by a test, a claim **proven as a code path** only, and a claim
**not proven by construction** because the hardware or platform to prove it does not exist
here. Read that table before trusting any "done" in this repository.

## Suite state on merged `main` (`83ab3f2`)

Measured from a clean worktree, not from CI summaries:

| Suite | Result |
|---|---|
| PlanCope.Central.Api.Tests | 100 passed |
| PlanCope.E2E.Tests | 3 passed |
| PlanCope.Local.Api.Tests | 76 passed, **1 skipped** |
| PlanCope.Local.Host.Tests | 31 passed |
| PlanCope.Shared.Grading.Tests | 50 passed |
| PlanCope.Shared.Tests | 106 passed |
| PlanCope.SyncCompat.Tests | 46 passed |
| ClientApp (vitest) | 85 passed, 11 files |
| **Total** | **497 passed, 1 skipped, 0 failed** |

The one skip is `EnrolmentEndpointsTests.Redeem_endpoint_placeholder`. Its skip reason states
no Local.Api `WebApplicationFactory` fixture existed that could fake outbound Central calls.
**That reason is now stale** — later batches built exactly such fixtures (see
`StatsEmptyResultTests`). The redeem endpoint is genuinely untested; the placeholder is an
honest marker, not a passing test, and closing it is small, well-defined work.

---

## Open defects

None known on `main`.

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

1. ~~**The production-snapshot data check (B0 task 1).**~~ **CLOSED — checked against real
   production data on 2026-09-16. The unique index is safe.** Details in
   [CUE uniqueness, checked against production](#cue-uniqueness-checked-against-production)
   below.

2. **B8's four performance criteria.** Cold start, 100-question render, idle CPU and the real
   Argon2id timing each need a **2-core / 4 GB / HDD Windows machine**. The figures currently
   recorded came from a 16-core development box and are labelled as such.

3. **The win-x64 Host cannot build in this environment at all.** WinForms + WebView2 does not
   compile on Linux, so R2R, trimming, DPAPI and hardware fingerprinting are unverified **by
   construction, not by omission**. This is why B9's E2E proves the *server* side of the gated
   update contract and explicitly refuses to claim the client side: an assertion that the
   update leg "did not throw" would pass identically on a machine with no update mechanism
   installed at all, which is exactly this one.

4. **Activation key distribution.** Keys are universal bearer secrets. How they reach 1,440
   schools without circulating in a group chat is the question this plan cannot answer, and
   `max_activations` is the only technical control limiting the damage if they do.

5. **Shipping unsigned.** Recorded in the plan as an accepted risk rather than a solved
   problem: it normalises clicking past a security warning on machines holding data for
   227,598 minors.

---

## Housekeeping

- **GitGuardian fails on PR #42** (`ci`, the only required check, passed). The finding is the
  literal `release-test-key-with-at-least-32-bytes`, a test-only HMAC key for document
  nominalization. It is **already committed on `main` in four test files**; GitGuardian scans
  diffs, so it stayed quiet on PRs that did not touch it and fired when B9 added a fifth
  occurrence. No credential was introduced. Dismissing it, or adding an ignore rule, needs
  dashboard access — **owner's call.**
- **PR #9**, opened 2026-09-09, is `DIRTY` and adds a `ci-cd-velopack-coolify-plan.md` that
  already exists at the repo root plus a batch plan that `PROJECT-CLOSURE-PLAN.md` supersedes.
  It looks obsolete — **closing it is the owner's call, not the coordinator's.**
- Plan drift is recorded in `PROJECT-CLOSURE-PLAN.md` rather than left to contradict the code.
  The Local migration sequence collided four times between parallel branches: B2's
  `009_NodeIdentity.sql` was renamed twice — to `010_NodeIdentity.sql` (after B8's
  `009_PerformanceIndexes.sql` merged first), then to `012_NodeIdentity.sql` (after B3's
  `010_ExamVersionScoringPolicy.sql`/`011_Grading.sql` merged) — both caught before merge, not
  after. B3's `010_Grading.sql` shipped as `011`, and B4's `011_Stats.sql` as `013`.
  `scripts/check-migration-numbers.sh` now guards this in CI.
- B7 ships a Velopack `releases.{channel}.json` feed in Velopack's own `VelopackAssetFeed`
  wire shape, **not** the generic authenticated JSON feed the plan originally described. The
  plan text carries a correction note rather than a silent rewrite.


---

## CUE uniqueness, checked against production

**Status: CLOSED.** B0 task 1 asked whether real data survives `core.schools."Cue"` becoming
unique. It does. This was not reasoned about — it was run.

### What was used as the production snapshot

plan_cope Central is **not deployed yet** (`il.sistemas.mec.gob.ar` answers `503`), so there is
no plan_cope production database to snapshot. That makes the honest question a different one:
**does the upstream registry that plan_cope ingests contain CUE collisions?** It is the source
data, not plan_cope's own table, that can carry a duplicate in.

The check therefore ran against the live provincial database (`asistencias`, PostgreSQL 17),
read-only, using its `secciones.cue_anexo` column — **2 059 distinct establishments**.

### The scare, and why it was wrong

Production stores establishments as `CUE-anexo`, e.g. `1800000-00` and `1800000-01`. Grouped on
the **7-digit CUE alone** the data looks alarming:

| Measure | Count |
|---|---|
| Distinct `cue_anexo` | 2 059 |
| Distinct 7-digit CUE | 1 563 |
| CUEs carrying more than one anexo | 176 |
| Establishments that would collide | **496** |

**That collision is not reachable, because plan_cope does not key on the 7-digit CUE.**
`CueCode` (`src/Shared/PlanCope.Shared.Domain/ValueObjects/CueCode.cs`) defines a CUE as
**exactly 9 digits** — it strips the separator and keeps CUE *and* anexo, so `1800000-00` and
`1800000-01` normalize to `180000000` and `180000001`, which are distinct. Applying that exact
rule to all 2 059 production values:

| Measure | Count |
|---|---|
| Normalized to exactly 9 digits | 2 059 |
| Rejected by `CueCode` (wrong length) | **0** |
| Distinct normalized values | 2 059 |
| **Collisions** | **0** |

Worth recording rather than deleting: the 7-digit reading was checked *first* and looked like a
496-row defect. What disproved it was the code, not an assumption — and the reverse mistake
(keying on the 7-digit CUE) is a real one someone could still make. `School` carries `Cue` and
`Annex` as separate columns, so the shape invites it.

### The migration, rehearsed on that data

Against a real `postgres:17-alpine`, from an empty database:

1. Migrated to `20260910004608_AddUserSchools`, the state immediately before the unique index.
2. Loaded all **2 059** real production CUEs into `core.schools`.
3. Applied `20260915220905_MakeSchoolsCueUnique` — **succeeded**. 2 059 rows in, 2 059 rows out,
   `indisunique = true`. Nothing was dropped or merged.
4. Reverted it — **succeeded**, index back to non-unique, all 2 059 rows intact.
5. **Made the guard fail on purpose.** Injected one duplicate `Cue` and re-applied. It refused,
   naming the offender: `core.schools has 1 duplicate Cue value(s) that must be resolved before
   the unique index can be created: 180000000`. A guard that never fires proves nothing; this
   one is proven in both directions.
6. Removed the duplicate and migrated to head — all **12** migrations applied over real data.

### What this does not prove

The upstream registry is live and can change. This is a measurement of the data **as of
2026-09-16**, not a permanent guarantee — which is exactly why the migration keeps its
pre-flight guard instead of trusting this result. If a future load does carry a duplicate, the
migration aborts with the offending CUE named rather than silently discarding a school.


## How this was run

`docs/DELEGATION-RULES.md` holds the fourteen rules this session established, each with the
failure that produced it. `scripts/LEVEL3-DISPATCH-PROTOCOL.md` holds the dispatch mechanics.
`BATCH-EXECUTION-STATUS.md` is the full review log, cycle by cycle, including the corrections
that turned out to be wrong.
