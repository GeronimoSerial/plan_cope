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

## Wave 2 — not yet dispatched

Tasks 7 (Local: grade on submission, migration `010_Grading.sql`) and 8 (Central:
recompute independently on ingest) — disjoint file trees (Local vs Central), to be
dispatched together once G1 lands and both wave-1 slices are confirmed merged clean.
