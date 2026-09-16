# B3 progress — Grading engine

## Wave 1 — tasks 1-6 (must land before 7/8)

Two disjoint slices, dispatched together per the fan-out protocol:

- **G1** — new `PlanCope.Shared.Grading` project: `IBlockGrader`, `ScoringPolicy` +
  strategies, `GradingEngine.Grade`, `GradingSchemaVersion`, golden fixtures + property
  tests. Files: `src/Shared/PlanCope.Shared.Grading/**`,
  `tests/PlanCope.Shared.Grading.Tests/**`, `PlanCope.slnx` (two new entries).
- **G2** — thread `scoringPolicy` through the exam document schema, publish checksum,
  and sync/Local pull, as an opaque string (no interpretation). Files:
  `ExamModels.cs`, `LocalModels.cs`, `ExamContracts.cs`, `ExamsController.cs`,
  `ExamPackageChecksum.cs` (new), `ExamEntityConfiguration.cs`, Central EF migration,
  `SyncController.cs`, `LocalExamPullService.cs`, `LocalExamRepository.cs`,
  `LocalDemoExamSeeder.cs`, Local migration `009_ExamVersionScoringPolicy.sql`, plus
  `ExamsContractTests.cs` / `PublishPullRunPushTests.cs` pin updates and two new tests.

### Dispatch history

| Attempt | Command | G1 result | G2 result |
|---|---|---|---|
| 1 | `opencode run ... g1-brief.txt` / `g2-brief.txt`, 420s | rc=124, timed out mid-exploration — never wrote a file | rc=124, timed out but landed partial work (contract fields, checksum extraction) |
| 2 | Same G1 brief + explicit "stop exploring, write files" note; G2 continuation brief listing exact remaining errors | rc=0, but **zero tool calls** — model spent its entire step generating prose and hit the 32k output-token cap ("reason":"length"), wrote nothing to disk | rc=124 again, but this time **finished everything**: migrations, repository wiring, both acceptance tests — confirmed by `dotnet build` going green afterward |

**Lesson applied (LEVEL3-DISPATCH-PROTOCOL rule 1 / brief lesson 1):** exit code alone
is not a verdict either way. rc=0 with zero bytes on disk is a dead run; rc=124 (killed
at the timeout) can still mean the work finished just before the kill. Diagnosed both
by `git status`/`git diff` + `dotnet build`, not by the shell exit code.

**G2 accepted after review:** build green, and I ran `dotnet test` per project
(Central.Api.Tests 32/32, Local.Api.Tests 27/27, SyncCompat.Tests 46/46, E2E.Tests 1/1),
then `dotnet test PlanCope.slnx` for the whole solution (125/125, zero failures) since
this slice touches `Shared.Contracts`/`Shared.Domain` that every side consumes.
Read every diff by hand: the checksum extraction is a genuine DRY win (one
`ExamPackageChecksum.Compute` now used at both call sites that used to inline the
payload separately), the two new tests are non-tautological (checksum-changes-on-
policy-change test, and a real HTTP-pull → SQLite persistence test, not a mock of the
thing being proven), migration numbering avoids collision with G1's future `010_Grading.sql`.

Committed as three reviewable stories, not one 18-file blob:
- `6e7410f` — contract shape (`ExamModels`, `LocalModels`, `ExamContracts`) + the two
  contract-pin tests that had to move with it (`ExamsContractTests`,
  `PublishPullRunPushTests`) — deliberate pin change, not incidental, stated in the
  commit body.
- `b72bb8d` — publication checksum (`ExamPackageChecksum` extraction, `ExamsController`,
  `ExamEntityConfiguration`, Central EF migration) + its test.
- `bd242c9` — sync/Local propagation (`SyncController`, `LocalExamPullService`,
  `LocalExamRepository`, `LocalDemoExamSeeder`, Local migration `009`) + its test.

**G1 re-dispatched alone** (task done, not landed) with a sharpened brief: explicit
instruction to write files via tool calls immediately rather than narrating a plan in
prose first, since the failure mode observed was "burned the whole turn on text, hit
the output cap, wrote nothing."

### Decision recorded: nullable `ScoringPolicy` today is scaffolding, not a design choice — reject, don't default, when grading actually reads it

Coordinator flagged (cycle 4) that `string? ScoringPolicy` on `ExamVersionDto` /
`PublishedExamPackageDto` / `CreateExamVersionRequest` has no rejection path yet, and
that this is exactly how implicit grading sneaks in. Decision, to bind whoever wires
`GradingEngine.Grade` into Local (task 7) and Central (task 8):

- **The engine throws `UngradableExamException`, loudly, the moment it needs a policy
  and has none — never a default, never a logged warning that proceeds anyway.** This
  is §2.7 and the §7 no-implicit-grading gate, non-negotiable.
- **Version skew is real and must be decided explicitly, not discovered in the field.**
  B0's `ContractToleranceTests` pin: an unknown field is ignored, a missing field
  becomes null. `scoringPolicy` is additive, so a Local node that pulled a package
  *before* this feature existed — or whose own `LocalExamVersion` row predates
  migration 009 — will have `ScoringPolicy == null` for exams that otherwise have one
  centrally. Two options were on the table:
  - Default such attempts to some policy → wrong marks for real students, invisible
    until someone audits scores.
  - Refuse to grade until the node is caught up (a fresh pull has the field) or a
    legacy-assignment record exists → a support call, immediately visible.
  **Decision: refuse.** A school that cannot grade raises a support call the same day;
  a school that grades wrong may never notice, per §2.7's own reasoning for why the
  engine refuses instead of guessing in the first place. This is the same failure mode
  the plan is already explicitly designed against, just triggered by version skew
  instead of by an exam predating the feature.
- **Scope note, so this isn't inherited by surprise:** this decision governs how B3's
  `GradingEngine.Grade` behaves when called with a null-resolved policy — that part is
  this batch's job (tasks 7/8, wave 2/3, not yet dispatched). It does **not** cover an
  operator-facing "your app is out of date, sync before grading" UX — that's a Local UI
  concern for whichever batch owns operator-visible messaging (B5's sync-state
  visibility, or B9's fail-path UX pass, own that copy). B3 only needs to make sure the
  underlying refusal is correct and typed; it is not writing the Spanish copy that
  explains it to an operator.

## Wave 2 — done: tasks 7 and 8, plus one prerequisite slice

### Prerequisite slice (2a): safe scoring-policy parsing and JSON mapping

Dispatched alone first because both task 7 and task 8 depend on it and it is the
single place this logic may live (2.4 / DRY). Two files in `PlanCope.Shared.Grading`:

- `ScoringPolicyParser.Parse(string?) -> ScoringPolicy?` — closes a real bug class
  flagged by the coordinator: `Enum.TryParse<ScoringPolicy>(raw, out var value)` sets
  `value` to the zero member (`AllOrNothing`) even when the parse **fails**, and also
  accepts numeric strings (`"0"` → `AllOrNothing`, `true`) and comma-separated
  flags-style lists. A naive `if (TryParse(...)) return value;` would silently grade
  an absent, malformed, or version-skewed policy field as `AllOrNothing` instead of
  refusing per §2.7/§7. The parser matches the trimmed input against the three known
  member names, case-insensitively, before ever trusting a parsed value. Verified as
  the *only* string-to-enum path for this type (grepped after the fact, per
  coordinator request — no duplicate/diverging parser exists anywhere).
- `GradingJsonMapper` — the single place that turns author answer keys and student
  submissions into the engine's plain types, so Local and Central cannot diverge on
  JSON shape. Canonical shapes (previously undefined in this codebase — invented once,
  here, not independently by each side): MultipleChoice correct/submitted answers are
  string arrays of option `value`s; TrueFalse is a JSON boolean; ShortAnswer's answer
  key is `{"accepted":[...]}`, submission is a plain string.

One dispatch typo of mine (test asserted `Parse("alloranothing")`, a misspelling,
expecting `AllOrNothing` — the parser was correct to reject it) was fixed directly in
review rather than re-dispatched. Test coverage was later extended (still in review,
not re-dispatched) with the specific `"0"` and comma-list cases the coordinator named,
plus one test proving the chain end-to-end: every string the parser rejects also
leaves `GradingEngine.Grade` throwing `UngradableExamException`, not just that the
parser itself returns null.

Commits: `753ecbe`, `bf27140`.

### Task 7 — Local: grade on submission (`e56694e`, tests `7bfa143`)

`GradeAttempt` runs inside the existing `SubmitWithOutboxAsync` transaction — same
commit as the status flip to `submitted` and the outbox insert, so a crash between
them cannot leave a submitted attempt with no result. A missing scoring policy never
blocks submission (offline delivery must not depend on an administrator having
assigned a policy yet); it persists an `attempt_results` row with `status =
"ungradable"` instead. New migration `010_Grading.sql`. The outbox payload gained one
new field, `examVersionRemoteId`, so Central can resolve its own copy of the exam and
grade independently — Local's computed score is deliberately **not** sent, per 2.5
(derived state is never synced).

Answer-key resolution joins through `remote_block_id`, not the local block id —
`local_exam_blocks.id` and `.remote_block_id` are equal only by convention for real
synced data (`LocalExamPullService` mirrors the central id 1:1); the demo seeder
deliberately uses different values, and a regression test (`Answer_key_join_survives_
local_and_remote_block_ids_differing`) seeds them differing on purpose to prove it.

First two dispatch attempts (`l1`, `l2`) both hit the 420s wall; diagnosed via
`git diff`/build rather than treated as failures per protocol. `l1` landed the full
implementation (verified correct on inspection + build + no regression on the
existing 27 tests) but no new tests. `l2` was re-dispatched for tests only and spent
its entire budget exploring the existing fixture pattern, writing nothing — the sharp
follow-up (`l3`) named the exact file (`LocalSessionFlowTests.cs`) and JSON shapes
directly, and landed cleanly with `"reason":"stop"`, not `"length"`.

Known flake, not a regression: `ExamScoringPolicyPullTests` failed once in a combined
run and passed on immediate re-run and in isolation. Cause: each `LocalApiFactory`-
style test fixture mutates process-wide `ConnectionStrings__LocalDatabase`/
`Local__SeedDemoExam` env vars in its constructor; xunit parallelises across test
classes by default, so two fixtures racing can step on each other. Pre-existing
pattern, more collision-prone now that a fifth test class uses it. Left unfixed —
out of this batch's scope; a handoff for whoever next touches the local test harness.

### Task 8 — Central: recompute independently on ingest (`1666dba`)

`RecomputeGradeAsync`, called from `AddAttemptAsync` when `examVersionRemoteId` is
present in the payload, loads Central's own `ExamVersion`/`ExamBlocks`/`AnswerKeys`
(Central authored the exam; it needs nothing from Local), maps through the same
`GradingJsonMapper`/`ScoringPolicyParser` Local uses, and grades from the
`ReceivedSubmissionAnswer` rows already being added in the same call — all in one
change-set, one `SaveChangesAsync`, one transaction. New EF migration
`20260916004033_AddAttemptResults`.

Resolved without a re-dispatch round-trip: whether `ReceivedSubmissionAnswer.BlockId`
is in the same id-space as Central's `ExamBlock.Id`. Read `LocalExamPullService.cs`
directly — it sets `LocalExamBlock.Id = block.Id` (the central id) on import, so yes,
no remapping needed. Handed to the implementer as a resolved fact rather than an open
question, per lesson 2 (name the existing file).

First attempt (`c1`) timed out at 420s having done only the scaffolding (DbSet, entity
config, domain record, csproj reference) — `SyncController.cs` itself was never
touched, no migration generated, no tests. Diagnosed via `git diff`/build before
re-dispatching. Sharp follow-up (`c2`), which explicitly said "the wiring was never
reached, do that first," completed the whole task including the specific regression
test the coordinator asked for (`ScoringPolicy = "NotARealPolicy"` in a seeded
database graded as `ungradable`, never silently as `AllOrNothing`).

**Not verified against a real Postgres** — none is reachable from this worktree. The
migration's correctness against a live database is unverified here; that is exactly
what the `central-migrations` CI job (merged in B0, applies every migration to a real
Postgres 17 and rehearses rollback) proves instead, not this environment.

### CI wiring gap, closed (`f1e318f`)

`tests/PlanCope.Shared.Grading.Tests` built and passed locally but ran in **no**
workflow — flagged by the coordinator, confirmed by grep. Fixed two ways, both
needed: added the project to `ci-central-api.yml`'s build/test loops (alongside the
other Shared test projects), **and** added `tests/PlanCope.Shared.Grading.Tests/**` to
`ci.yml`'s `shared` path filter — without the second part, a PR touching only that
test project (not `src/Shared/**`) would still skip the job entirely. Not duplicated
into `ci-local-app.yml`: the engine is pure C# with no OS-specific behaviour, so
testing it once on `ubuntu-latest` is sufficient even though `Local.Api` also now
references the project.

### Handoff — blocked on the coordinator, not on B3

B1 (#21) has not yet merged to `main`. Per the coordinator (cycle 16): do not rebase
onto a moving target. When B1 merges:
1. Rebase onto `main`.
2. Renumber Local migrations: B8's `009_PerformanceIndexes.sql` already claimed 009
   before this batch could see it, so **`009_ExamVersionScoringPolicy.sql` → `010`,
   `010_Grading.sql` → `011`** (rename only, never edit an applied migration, never
   merge the two into one — they must stay independently revertible).
3. Reconcile `src/Shared/PlanCope.Shared.Domain/Central/SyncModels.cs` by taking
   **both** B1's changes (activation keys/node credentials) and this batch's
   (`CentralAttemptResult`) — not by picking a side. Check for overlapping/near-
   duplicate concepts per the coordinator's DRY concern before considering it done.

Task 9 (re-grade command, additive, writes a new `grading_schema_version` row without
overwriting a prior result) is the only B3 main task not yet started. Not begun this
cycle per explicit coordinator instruction ("do not begin new tasks" while blocked on
the B1 merge/rebase) — next up once the rebase lands.
