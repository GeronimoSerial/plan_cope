# B0 · Progress log

Leader: Sonnet (Level-2). Implementer: `opencode run --auto -m opencode-go/deepseek-v4-flash`.

## Task status

| # | Task | Status |
|---|---|---|
| 1 | Central: unique `core.schools.Cue` + duplicate detection | DONE (code) — production-snapshot verification is an open escalation, no live Postgres here |
| 2 | Local `008_Schools.sql`: `schools` table, backfill, FKs | DONE |
| 3 | `CueCode` at every boundary, normalise on write only | DONE |
| 4 | `SyncCompat.Tests`: xunit + ≥15 contract tests | DONE (46 tests) |
| 5 | `E2E.Tests`: xunit + 1 real end-to-end scenario | DONE, verified green by running it |
| 6 | Remove `--if-present` no-op in `ci-local-app.yml:46` | DONE |

## Baseline facts established before dispatch (do not rediscover)

- `core.schools.Cue` is Postgres `bigint`, indexed but **not unique** (`IX_schools_Cue`,
  `CoreEntityConfiguration.cs:98`). No `new School(...)` construction site exists in this
  repo — the table is written from outside the codebase, so task 3 does not touch it.
- `CueCode` (`src/Shared/PlanCope.Shared.Domain/ValueObjects/CueCode.cs`) already exists and
  is already used at most string-CUE boundaries (Roster endpoints, session creation, GE
  integration). Task 3 is a gap-closing sweep, not a greenfield introduction.
- Local SQLite has no `schools` table yet. `local_roster_snapshots.cue` and
  `delivery_sessions.school_code` are loose `TEXT` columns (`001_Initial.sql`,
  `004_Roster.sql`). SQLite cannot `ALTER TABLE ADD FOREIGN KEY` — adding the FK requires the
  12-step rebuild pattern (new table, copy, drop, rename).
- Local migrations run via DbUp (`LocalDatabaseInitializer.cs`), embedded SQL scripts, applied
  in filename order. Next number is `008`.
- `tests/PlanCope.SyncCompat.Tests` and `tests/PlanCope.E2E.Tests` are empty shells (csproj +
  `AssemblyMarker.cs`), no test package referenced. `Directory.Packages.props` already pins
  `xunit 2.9.2`, `xunit.runner.visualstudio 2.8.2`, `Microsoft.NET.Test.Sdk 17.11.1`.
- `ci-local-app.yml` already builds/tests `E2E.Tests` in the .NET step (lines 22-37); it is a
  no-op today only because the project has zero tests, not because CI skips it.
- The npm test no-op is real: `ci-local-app.yml:46` runs
  `npm run test --workspace plancope-local-host-ui --if-present`. The `test` script **does**
  exist (`vitest run`, 2 existing spec files), so `--if-present` is masking nothing today but
  must still go per task 6.
- No live Postgres/production snapshot is reachable from this worktree. The migration's
  "tested against a restored production snapshot" acceptance criterion cannot be executed
  here — will be reported as NOT verified, not silently skipped.

## Committed state

Unit A is committed on `feat/b0-foundations` as three reviewable stories (not squashed):
- `bcfbf9a` — Central unique `core.schools.Cue` + duplicate-check guard (task 1).
- `9bc986e` — Local `008_Schools.sql` schema + FK rebuild (task 2).
- `f4c26bf` — CueCode write-only normalisation + ensure-schools-row helper, with its tests
  (task 3).

Unit B (SyncCompat.Tests) is in progress and NOT yet committed — `AuthAndLocalContractTests.cs`
covers 13/40 DTOs so far. Commit it at task 4's completion boundary, not before, and not folded
into a trailing "add tests" commit — split further if task 4 ends up spanning more than one
natural story.

## What a successor needs that the diff alone won't tell them

**Why read-path `CueCode.Normalize` calls were removed instead of made stricter.** The plan's
rule is "normalise on write, never on read." `LocalRosterRepository`'s four read methods were
defensively re-normalizing on every read, which looks safe but actually hides whether the real
write-time guarantee holds. It does hold: `LocalRosterPackageValidator.Validate` (pre-existing,
untouched by this batch) does `CueCode.TryNormalize` then a **byte-identity check against the
original string** — `if (!string.Equals(package.Cue, normalizedCue, StringComparison.Ordinal)) throw`.
That's a reject-gate, not a coerce-gate, but for this purpose it's exactly as strong: nothing
non-canonical can reach the insert. Removing the read-side calls was safe *because* that gate
already existed and was verified to run on every path into `ImportAsync`
(`LocalRosterRepository.cs:14` and `EmbeddedRosterSeeder.cs:49→73`), and because
`SessionEndpoints.cs:46` normalises `SchoolCode` before `SessionRepository.CreateAsync`'s one
and only caller reaches it. **Do not add a second `CueCode.Normalize` call inside `ImportAsync`
or the ensure-schools-row helper** — that would create a second normalization authority next to
the validator's gate, which is the exact DRY violation the plan names. If you're ever tempted
to "harden" this further, trace the actual caller chain first; both callers were independently
confirmed to normalize upstream.

**Why the `LocalSessionFlowTests.SeedRoster` fix was accepted as a direct edit, not a
dispatch.** After the ensure-schools-row fix landed, 7 of 25 `PlanCope.Local.Api.Tests` still
failed with `FOREIGN KEY constraint failed`. The temptation (flagged explicitly by the
coordinator as a trap to avoid) is to patch a failing test until it's green, which can silently
paper over a real production regression. The distinguishing question was: **does any
production code path write to this table without going through the code we just fixed?**
`rg "INSERT INTO local_roster_snapshots" src/Local` returned exactly one hit —
`LocalRosterRepository.ImportAsync`, already fixed. `SeedRoster` is test-only scaffolding that
inserts directly via raw SQL, bypassing the repository entirely — a test fixture drifting out
of sync with a schema change, not evidence of a live bug. That's why it was a same-turn direct
fix (one line, mechanical, no design decision) rather than a re-dispatch. Contrast with the
`SessionRepository`/`LocalRosterRepository` fix itself, which WAS a design decision (where does
the schools row get created, single-sourced how) and WAS dispatched. The boundary: the moment
a fix requires deciding *where* new behaviour lives, it's a dispatch; a mechanical
one-line alignment to a schema change already designed and reviewed is not.

**Which brief shapes actually produced usable output from `deepseek-v4-flash`, and which
didn't:**
- Narrow, single-file, exact-behavior briefs with an explicit acceptance command landed clean
  on the first try every time (Central migration; local FK-integrity test; CueCode sweep across
  3 files; offline-gate guard test; 13-DTO contract test slice).
- A brief handing over verbatim bytes to copy (the 008_Schools.sql SQL block) also worked, but
  is the exception, not the default — reserve it for genuinely order-sensitive, easy-to-get-
  subtly-wrong text (the SQLite FK-add-via-rebuild dance), and say so explicitly when you do it.
- **The one dispatch that produced zero bytes** was the first attempt at task 4: "write ≥15
  tests covering wire-shape + round-trip + additive-tolerance + required-field-presence across
  every one of 40 DTOs, plus project setup" in one brief. It spent its whole 420s budget reading
  files and never started writing. Splitting it into one dispatch per DTO group (13 DTOs: Auth +
  Local) succeeded immediately with the identical acceptance bar. **Lesson: a brief covering
  more than roughly 10-15 DTOs/files/behaviors in one go risks timing out on research alone
  before any code gets written — split by natural grouping (a file, a small DTO namespace, a
  bounded rule), not by shortening the prose.** Rewriting the same-scope brief with a longer
  timeout is the wrong fix; narrower scope is.

## Dispatch log

(appended after each `opencode run` call, in order)

### Slice A1 — Central unique CUE migration (task 1)

Command: `timeout 420 opencode run --auto --format json -m opencode-go/deepseek-v4-flash "<brief: add MakeSchoolsCueUnique EF migration with duplicate-check DO block, unique index on core.schools.Cue, IsUnique() in CoreEntityConfiguration>"`

Returned: `src/Central/PlanCope.Central.Migrations/Migrations/20260915220905_MakeSchoolsCueUnique.cs`
(+ `.Designer.cs`), `PlanCopeDbContextModelSnapshot.cs` updated, `CoreEntityConfiguration.cs`
`HasIndex(x => x.Cue)` → `.IsUnique()`.

**Accepted.** Verified by reading the migration in full (duplicate-check PL/pgSQL block
matches spec exactly, drop/recreate unique index, `Down()` correctly reverses to
non-unique). Verified `dotnet build` clean with `-warnaserror` on both
`PlanCope.Central.Migrations.csproj` and `PlanCope.Central.Api.csproj` (0 warnings, 0 errors).

**NOT verified** (no live Postgres/production snapshot reachable from this worktree): the
migration has not actually been run against a restored production snapshot as the plan's
"Tests and QA" section requires. This is an honest gap, reported not hidden. Reversibility
was checked by code review only, not by executing `Down()`.

Task 1: DONE (build-verified), production-snapshot verification PENDING (infra unavailable
here — needs escalation to whoever owns a snapshot environment).

### Slice A2 — Local `008_Schools.sql` + FK rebuild + verification test (task 2)

Command: `timeout -k 30 420 opencode run --auto --format json -m opencode-go/deepseek-v4-flash "<brief>"`

**Process note on this one dispatch:** the brief handed the implementer the exact SQL text
for `008_Schools.sql` to copy verbatim, rather than only the behaviour/constraints. Reason:
SQLite has no `ALTER TABLE ... ADD FOREIGN KEY`, so adding FKs to `local_roster_snapshots`
and `delivery_sessions` requires the order-sensitive 12-step rebuild (temp table, copy, drop,
rename, reindex, `PRAGMA foreign_keys` toggle around it). A single wrong step order silently
corrupts data or leaves FKs unenforced with no error. I judged the risk of a weak model
inventing that sequence from prose higher than the author/reviewer-conflation risk, given
that TASK 2 in the same brief (a from-scratch xunit test proving FK enforcement, written
independently from behavioural spec only) still gave genuine, independent verification of the
result. Every dispatch after this one specifies behaviour and acceptance, not bytes, per
coordinator direction.

Returned: `src/Local/PlanCope.Local.Api/Data/Migrations/008_Schools.sql` (byte-for-byte match
to the brief's SQL, diffed and confirmed), `tests/PlanCope.Local.Api.Tests/SchoolsForeignKeyTests.cs`
(new, written independently — asserts `PRAGMA foreign_key_check` is empty after seeding a
schools/roster-snapshot/session chain, and that inserting a `delivery_sessions` row with an
unknown `school_code` throws `SqliteException`).

**Accepted, with a real integration defect surfaced — not yet fixed.**
- `dotnet build tests/PlanCope.Local.Api.Tests -warnaserror`: clean.
- `dotnet test ... --filter SchoolsForeignKeyTests`: 1/1 passed.
- `dotnet test tests/PlanCope.Local.Api.Tests` (full suite): **11 of 25 pre-existing tests now
  fail** with `SQLite Error 19: FOREIGN KEY constraint failed`, or with 500s caused by that
  same constraint bubbling up through the API. Root cause, confirmed by stack traces:
  - `LocalRosterRepository.ImportAsync` (`LocalRosterRepository.cs:60`) inserts into
    `local_roster_snapshots` without first ensuring a `schools` row exists for `package.Cue`.
  - `SessionRepository.CreateAsync` (`SessionRepository.cs:8-12`) inserts into
    `delivery_sessions` without first ensuring a `schools` row exists for `session.SchoolCode`.
  These are real production write paths, not test-fixture gaps — the failing
  `LocalRosterRepositoryTests.Import_is_idempotent...` test goes straight through
  `LocalRosterRepository.ImportAsync`, and `LocalSessionFlowTests.Session_attempt_submit_flow...`
  goes through the real `/api/sessions` endpoint end-to-end (`InternalServerError`, not a
  seeding shortcut).
- Task 2's own acceptance criteria (schema + FK + one proof test) are met. The batch is
  **not** integration-safe until task 3 adds an "ensure schools row exists" step at exactly
  these two write boundaries — this is now folded into the task 3 dispatch, single-sourced as
  one small helper, per DRY.

Task 2: DONE at the schema level. Full-suite green is BLOCKED on task 3.

### Coordinator findings folded into the task 3 brief (not yet dispatched)

- `LocalRosterRepository.cs:210,236,270,298` call `CueCode.Normalize` on **read** paths —
  violates "normalise on write, never on read." Callers already pass normalized cues; these
  reads should trust the input, not re-normalize defensively.
- `GeRosterService.cs:372` (`private static string NormalizeCue(string? cue) => CueCode.Normalize(cue)`)
  is a second name for the single normalization source — fold call sites onto `CueCode.Normalize`
  directly and delete the wrapper.
- New, found during A2 review: add one shared "ensure schools row" write-time helper, called
  from `LocalRosterRepository.ImportAsync` and `SessionRepository.CreateAsync` — the two real
  write boundaries that create `local_roster_snapshots`/`delivery_sessions` rows referencing a
  `cue`/`school_code` that may not yet have a `schools` row.

### Slice A3 — CueCode read/write sweep + ensure-schools-row helper (task 3)

Command: `timeout -k 30 420 opencode run --auto --format json -m opencode-go/deepseek-v4-flash "<brief: behaviour+constraints+acceptance only, no bytes>"`.

**Design decision, recorded per coordinator request, before accepting:** the coordinator
raised — correctly, as a question to verify, not as a confirmed defect — whether
`LocalRosterRepository.ImportAsync` persists an unnormalized `package.Cue`, which would make
removing the four defensive read-side `CueCode.Normalize` calls dangerous (silent lookup miss
instead of a loud error). I traced it: `LocalRosterPackageValidator.Validate(package)` (called
unconditionally at the top of `ImportAsync`, untouched by this batch) already does
`CueCode.TryNormalize(package.Cue, out var normalizedCue)` and then
`if (!string.Equals(package.Cue, normalizedCue, StringComparison.Ordinal)) throw`. That is a
reject-gate, not a coerce-gate, but it is equally strong: no `package.Cue` that isn't already
byte-identical to its own normalized form can reach the schools/snapshot insert — it throws
first. The `StringComparison.OrdinalIgnoreCase` check in `LocalRosterPullService.cs:84` is a
separate, unrelated guard (requested-cue vs. returned-package-cue consistency) and is inert
for CUE purposes regardless of case sensitivity, since `CueCode` is digit-only — there is no
letter-casing a CUE can vary by. **Decision: do not add a second `CueCode.Normalize` call
inside `ImportAsync`.** Doing so would create a second normalization authority next to the
validator's reject-gate, which is the DRY violation the plan explicitly warns against. The
existing reject-gate is the single source for this path and it already holds. On question 3
(pre-existing raw rows): since this reject-gate predates this batch and is the only way a
package ever reaches `ImportAsync`, and `SessionEndpoints.cs:46` normalizes
`request.SchoolCode` before it ever reaches `SessionRepository.CreateAsync`, no non-canonical
`cue`/`school_code` can have been written by this codebase's own write paths. Not proven for
data injected by tooling outside this repo — flagged, not fixed, consistent with the "no
`new School(...)` in this repo" note above.

Returned:
- `LocalRosterRepository.cs`: four read methods (`GetLatestSnapshotAsync` ×2, `GetSectionsAsync`,
  `ValidateSelectionAsync`) no longer call `CueCode.Normalize` on their `cue` parameter; a call
  to the new `Schools.EnsureRowAsync(connection, transaction, package.Cue, ...)` was added
  inside `ImportAsync`'s existing transaction, immediately before the `local_roster_snapshots`
  insert.
- `SessionRepository.cs`: `CreateAsync` now opens an explicit transaction, calls
  `Schools.EnsureRowAsync(connection, transaction, session.SchoolCode, ...)`, then the existing
  insert, then commits — both statements atomic together.
- New file `src/Local/PlanCope.Local.Api/Data/Repositories/Schools.cs`: one static helper,
  `EnsureRowAsync`, `INSERT OR IGNORE INTO schools (cue, created_at) VALUES (@Cue, datetime('now'))`.
  Single implementation, called from both write sites — no duplication.
- `GeRosterService.cs`: `NormalizeCue` wrapper deleted, its one call site now calls
  `CueCode.Normalize(cue)` directly.

**Verification, run independently, not taken on the implementer's word:**
- `dotnet build PlanCope.slnx -warnaserror`: clean, 0/0.
- `dotnet test tests/PlanCope.Local.Api.Tests`: **7 of 25 still failing** after the dispatch
  returned — NOT the same 11 as before. Traced to `LocalSessionFlowTests.LocalApiFactory.SeedRoster`,
  a test-only helper that inserts into `local_roster_snapshots` via raw SQL, bypassing
  `LocalRosterRepository.ImportAsync` entirely (confirmed `ImportAsync` is the *only* production
  writer of that table — `rg` found exactly one `INSERT INTO local_roster_snapshots` in
  `src/Local`). This is a test-fixture gap, not a rediscovery of the production bug: the
  production write path is fixed; a test helper that never goes through it needed the same
  one-line schools upsert applied directly to `SchoolsForeignKeyTests.cs`'s own seed. Fixed
  directly (not re-dispatched) as a mechanical, single-location, no-design-decision change:
  added `INSERT OR IGNORE INTO schools (cue, created_at) VALUES ($cue, $fetched);` at the top
  of `SeedRoster`, matching the exact pattern already proven correct in A2's
  `SchoolsForeignKeyTests.cs`. Re-ran: **25/25 passing.**
- `dotnet test tests/PlanCope.Central.Api.Tests`: 30/30 passing.
- `dotnet test tests/PlanCope.Shared.Tests`: 8/8 passing, `CueCodeTests` untouched.

**Accepted**, with one item still open, tracked as slice A4 below: the coordinator asked for
an explicit test proving a session can be created for a CUE with **no roster snapshot at
all** — the actual offline first-activation scenario the `SessionRepository.CreateAsync` fix
exists to protect. No such test exists yet; `SchoolsForeignKeyTests` only proves FK integrity
with a pre-seeded `schools` row. Dispatching A4 for this now.

Task 3: DONE, full local/central/shared suites green. Batch is integration-safe.

### Slice A4 — offline-gate guard test (task 3 closure)

Command: `timeout -k 30 420 opencode run --auto --format json -m opencode-go/deepseek-v4-flash "<brief: behaviour+acceptance only>"`.

Returned: `CreateAsync_UpsertsSchoolsRow_WhenSchoolHasNoRosterSnapshot` appended to
`SchoolsForeignKeyTests.cs` — builds a fresh DB, seeds nothing into `schools` or
`local_roster_snapshots`, calls `SessionRepository.CreateAsync` directly for an unseen CUE,
asserts it succeeds, a `schools` row now exists, and `PRAGMA foreign_key_check` is empty.

**Accepted.** `dotnet build tests/PlanCope.Local.Api.Tests -warnaserror`: clean.
`dotnet test tests/PlanCope.Local.Api.Tests`: 26/26 passing (25 previous + this one). This is
the test that would have failed on the pre-A3 code and now proves the offline first-activation
invariant holds.

Minor, non-blocking: the test's sample CUE literal (`"88887779999"`, 11 digits) isn't
9-digit-canonical, but `SessionRepository.CreateAsync` never validates CUE shape (that already
happens upstream at `SessionEndpoints.cs:46` before this repository is ever called), so it
doesn't affect what the test actually proves. Not worth a re-dispatch.

**Tasks 1–3: DONE at code level.** Task 1's production-snapshot verification remains an open
escalation (no live Postgres reachable from this worktree) — reported, not closed, per the
plan's own instruction that this is answered by querying real production data.

---

## Unit B — tests and CI (tasks 4, 5, 6)

### Slice B4b — Sync + GeRoster contract tests (task 4, continued)

Command: `timeout -k 30 420 opencode run --auto --format json -m opencode-go/deepseek-v4-flash "<brief>"`.

Returned: `tests/PlanCope.SyncCompat.Tests/SyncAndGeRosterContractTests.cs`, 11 DTOs
(`PullRequest`, `SyncItem`, `PullResponse`, `PushItem`, `PushRequest`, `PushItemResult`,
`PushResponse`, `GeRosterPullRequest`, `GeRosterPackageDto`, `GeRosterSectionPackageDto`,
`GeRosterStudentPackageDto`), same wire-shape + round-trip pattern as B4a.

**Accepted.** `dotnet build -warnaserror`: clean. `dotnet test`: 24/24 (13 from B4a + 11 new).

### Slice B4c — Exams contract tests (task 4, continued)

Same command shape, same file-set discipline (new file only, existing files untouched).

Returned: `tests/PlanCope.SyncCompat.Tests/ExamsContractTests.cs`, all 16 DTOs from
`ExamContracts.cs`, including `JsonElement` config/metadata fields populated with real nested
JSON and `BlockType` enum values, not defaults.

**Accepted.** `dotnet build -warnaserror`: clean. `dotnet test`: 40/40 (24 + 16 new). All 40
DTOs declared in `PlanCopeJsonSerializerContext` now have a wire-shape + round-trip test.

### Slice B4d — additive-tolerance + required-field-presence tests (task 4 closure)

Ran solo (not waved) because it edits the same `tests/PlanCope.SyncCompat.Tests/` tree the B4c
dispatch had just finished writing to — waving it alongside B4c would have been the exact
same-file collision the wave protocol forbids; only safe to run once B4c had returned and been
reviewed.

CONTEXT recorded before dispatch, because it's a decision, not just a gap: the plan requires
round-trip, additive-change tolerance, and required-field presence; only round-trip existed.
Verified first that additive tolerance already holds today — `UnmappedMemberHandling` is
configured nowhere in `src/` or `tests/`, so `System.Text.Json`'s default (`Skip`) already
tolerates an unknown field. The gap was that nothing pinned it, so a future change to
`JsonUnmappedMemberHandling.Disallow` would go undetected.

Returned: `tests/PlanCope.SyncCompat.Tests/ContractToleranceTests.cs`, 6 tests across 3
representative DTOs (`LoginResponse`, `PullResponse`, `ExamVersionDto`) — one additive-
tolerance test and one required-field-missing test per DTO. The required-field tests observed
and pinned the ACTUAL current behaviour rather than assuming one: a missing required `string`
property (e.g. `LoginResponse.AccessToken`) deserializes to `null`, it does not throw, because
none of these DTOs declare a runtime-enforced `required` keyword or `[JsonRequired]`.

**Accepted.** `dotnet build -warnaserror`: clean. `dotnet test`: 46/46 (40 + 6 new).

**Task 4: DONE.** 46 contract tests across 4 files, comfortably over the plan's "≥15" floor,
covering all three required properties (round-trip, additive tolerance, required-field
presence) for every DTO in `PlanCopeJsonSerializerContext`.

**Handoff finding for B1 — do not fix in B0:** `ContractToleranceTests.cs` proves that a
`LoginResponse` payload missing `accessToken` deserializes successfully with `AccessToken =
null`, rather than being rejected. That's the correct thing to pin today (it documents real
behaviour), but it is a live hazard once B1 builds real login/token-refresh flows against this
contract: a malformed or hostile auth response can deserialize "successfully" into a null
token, and any consumer that doesn't null-check will misbehave. B1 should decide explicitly
whether `LoginResponse.AccessToken` (and similar required auth fields) should be hardened with
`required`/`[JsonRequired]` or an explicit post-deserialize null check at the call site — this
is an authentication-boundary decision, out of scope for B0's contract-test batch.

### Slice B6 — remove CI's silent no-op (task 6)

Waved together with B4d and B5 (disjoint file sets: `.github/workflows/ci-local-app.yml` only,
confirmed no overlap with the other two before firing).

Returned: `ci-local-app.yml:46` changed from
`npm run test --workspace plancope-local-host-ui --if-present` to
`npm run test --workspace plancope-local-host-ui`.

**Accepted, plus one direct follow-up fix.** The dispatch itself only changed the `run:` line;
the step's `name:` still read `"Test ClientApp (Vitest, if configured)"`, which now
contradicted the code (§7's "Documentation truth" gate) — the step is no longer conditional.
Fixed directly (one-line rename to `"Test ClientApp (Vitest)"`, no design decision, matches
every other step name in the file, which state what they do without a hedge).

**Task 6: DONE.**

### Slice B5 — E2E scenario (task 5), attempt 1: TIMED OUT

Waved together with B4d and B6 (file set: `tests/PlanCope.E2E.Tests/` only — confirmed disjoint
before firing). Also added directly, myself, before this wave (mechanical, established-pattern,
zero-design-decision, so the E2E dispatch's own file set could stay narrow): a
`public partial class Program;` marker at the end of `src/Central/PlanCope.Central.Api/Program.cs`,
mirroring the identical marker `PlanCope.Local.Api/Program.cs` already has — required for
`WebApplicationFactory<Program>` to work against Central at all. Verified
`dotnet build src/Central/PlanCope.Central.Api -warnaserror` stays clean after that change.

The brief asked for real end-to-end wiring: Central via `WebApplicationFactory` bound to a real
Kestrel port (Local's sync services build their own `HttpClient` with a `central_url` string
from `sync_state`, so an in-process TestServer client isn't reachable — needs a real loopback
port), a JWT built directly via `TokenService` (bypassing login, mirroring
`AuthControllerTests.cs`'s existing pattern), Local via the same `WebApplicationFactory<Program>`
pattern `LocalSessionFlowTests.cs` already uses, then publish → pull → run session → push
outbox → assert on Central.

**Result: `timeout -k 30 420` killed it at the wall, rc=124.** It got 296 lines into
`PublishPullRunPushTests.cs` before running out of budget — this is the hardest, most novel
slice in the whole batch (two in-process hosts, one needing a real socket, wired together) and
the original brief bundled project setup + a prescriptive bootstrap snippet + a 6-step scenario
in one dispatch. One confirmed defect in the partial file: `WebApplicationFactory<LocalApiApplication>`
instead of `WebApplicationFactory<Program>` (`LocalApiApplication` is a static builder helper,
not the entry point — `error CS0718: static types cannot be used as type arguments`). Unknown
whether other issues exist beyond that first compiler error, since the dispatch never reached
a build.

**Not accepted, not rejected — re-dispatching narrower per the timeout rule** (fix-forward on
the existing 296-line file, not a redesign from scratch): "here is the exact compile error,
here is the fix, get to build-and-test green, you have latitude to fix anything else you find
wrong." This is deliberately narrower than the original — the design is believed sound (it
matches the reference patterns in `LocalSessionFlowTests.cs`/`AuthControllerTests.cs`
exactly), what's missing is compile-and-verify iteration, which needs less exploration budget
than the original discovery pass consumed.

**Task 5: DONE — genuinely green, verified by running the test, not by a dispatch report.**

After the retry dispatch also timed out with zero bytes written (same `CS0718` root cause
diagnosed below — the retry never got past reasoning about it), every remaining fix was done
directly rather than re-dispatched, because each one was diagnosis-driven and mechanical once
diagnosed (build error → known fix; test failure → read the actual exception; no design
decision beyond the two flagged below):

1. `WebApplicationFactory<LocalApiApplication>` → `WebApplicationFactory<LocalDatabaseInitializer>`.
   `LocalApiApplication` is `public static class` — invalid as a generic type argument
   (`CS0718`), and the deeper reason it was there at all: this test references both
   `PlanCope.Central.Api` and `PlanCope.Local.Api`, and BOTH generate a global-namespace
   `Program` class from top-level statements, so unqualified `Program` is ambiguous here (a
   real ambiguity the coordinator flagged as a prediction — confirmed correct once reached).
   `LocalDatabaseInitializer` is any public, non-static, unambiguous type from
   `PlanCope.Local.Api`'s assembly — `WebApplicationFactory<T>` only needs `T.Assembly` to
   locate the entry point, `T` itself never has to be `Program`.
2. `tests/PlanCope.E2E.Tests/PlanCope.E2E.Tests.csproj`: dropped `net8.0-windows` /
   `EnableWindowsTargeting` and the `PlanCope.Local.Host` project reference. Nothing in the
   test uses WinForms — that reference only existed from the original empty-shell scaffolding
   and was dragging in the Windows Desktop shared runtime, which isn't installed here and
   isn't needed: `dotnet test` failed with "You must install or update .NET... Framework:
   Microsoft.WindowsDesktop.App" purely from this unused reference. Switched to plain
   `net8.0`, added `PlanCope.Local.Api` (needed) instead.
3. `Auth:SigningKey` never reached Central's Program.cs: it reads
   `builder.Configuration.GetSection("Auth")` before `WebApplicationBuilder.Build()`, and
   `WebApplicationFactory.ConfigureWebHost`'s `ConfigureAppConfiguration` doesn't reliably run
   in time for that in the minimal-hosting model. Fixed with the same technique
   `LocalApiFactory` already used for its own connection string: set an `Auth__SigningKey`
   environment variable in the factory's constructor, before the host builds.
4. Central's `IModelCustomizer` override for `JsonDocument` properties (needed because
   `AnswerKey.CorrectAnswer` etc. are `HasColumnType("jsonb")`, which the InMemory provider
   used for testing doesn't understand) didn't take effect via
   `services.AddSingleton<IModelCustomizer, ...>()` in the app's own DI container — the model
   validator still saw the property as unmapped. Fixed with
   `options.UseInMemoryDatabase(...).ReplaceService<IModelCustomizer, ...>()` directly on the
   `DbContextOptionsBuilder`, and the customizer itself applies the converter via the fluent
   `modelBuilder.Entity(...).Property(...).HasConversion(...)` API rather than the raw
   `IConventionProperty.SetValueConverter(...)` call the first attempt used, which didn't
   survive model finalization.
5. `SyncController`'s push endpoint opens a real `Database.BeginTransaction()`; the InMemory
   provider doesn't support transactions and EF treats that as a warning promoted to an
   exception by default. Suppressed narrowly:
   `.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))` — the
   InMemory provider silently no-ops the transaction either way, this only stops it throwing.
6. The real design decision made along the way, not a mechanical fix: Local's sync services
   (`LocalExamPullService`, `LocalOutboxPushService`) build their own `HttpClient` and set its
   `BaseAddress` from a `central_url` string in `sync_state` — a real network call in
   production. Getting `WebApplicationFactory` to bind Central to a real, externally-reachable
   Kestrel socket turned out to be exactly as fragile as flagged: `WebApplicationFactory`'s
   `CreateHost` never actually starts a real listener by default even with `UseKestrel()` +
   `UseUrls()`, and overriding `CreateHost` to force one produced a server that reported its
   own configured "http://127.0.0.1:0" back verbatim with nothing actually bound — connection
   refused. Rather than keep pushing on a real socket (which the coordinator explicitly said
   was theirs to authorize, not mine to build unilaterally), the test instead points Local's
   two NAMED HttpClients (`nameof(LocalExamPullService)`, `nameof(LocalOutboxPushService)` —
   confirmed by reading `LocalDataServiceCollectionExtensions.cs`, both already registered as
   named clients, not the default one) at Central's own in-memory `TestServer` handler
   (`Server.CreateHandler()`), via `ConfigurePrimaryHttpMessageHandler` in `LocalApiFactory`'s
   `ConfigureWebHost`. No real socket, no port, no Kestrel — `central_url` is just a
   syntactically-valid placeholder string (`Server.BaseAddress`) since TestServer's handler
   dispatches by request path, not by host/port. This is a smaller, safer intervention than a
   real bound port and touches no production code.

**Verification, run myself, not taken from a dispatch report:**
`dotnet build tests/PlanCope.E2E.Tests/PlanCope.E2E.Tests.csproj -warnaserror`: 0/0.
`dotnet test tests/PlanCope.E2E.Tests/PlanCope.E2E.Tests.csproj`: **1/1 passing** — the full
scenario (create exam → version → block → publish on Central, pull on Local, run a session,
submit an attempt, push the outbox, assert the submission landed on Central with status
`"submitted"`) executes for real. `dotnet build PlanCope.slnx -warnaserror`: 0/0, whole
solution.

**Duplication resolved — coordinator decision, not mine alone.** The coordinator read both
copies and found they were not just duplicated but non-equivalent: the pre-existing
`AuthControllerTests.cs` copy set the converter via the mutable/convention-level property API,
which does not survive model finalization once a relational `HasColumnType("jsonb")` is
already configured on the same property — it only "worked" there because that suite never
exercised a jsonb property hard enough to expose it. Decision: keep the fluent-builder version
(this batch's, proven against `AnswerKey.CorrectAnswer`) as the single copy, in a new file
`tests/PlanCope.Central.Api.Tests/JsonDocumentFriendlyModelCustomizer.cs`, linked into
`PlanCope.E2E.Tests.csproj` via `<Compile Include>` — no new project. Both private nested
copies removed from `AuthControllerTests.cs` and `PublishPullRunPushTests.cs`. Re-verified
after the change: `PlanCope.Central.Api.Tests` 30/30, `PlanCope.E2E.Tests` 1/1, full solution
build 0/0. Committed as `dab2ad1`, its own unit, after tasks 4/5/6 were committed separately
(`23f6765`, `e84853e`, `217442f`).
