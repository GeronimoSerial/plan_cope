# Batch execution status

Coordinator-owned (Opus). Updated on a 5-minute review cycle. Leaders do not write here —
they write `_briefs/B<n>-PROGRESS.md` inside their own worktree, and the coordinator
verifies those claims against `git diff` and `pgrep -x opencode` before recording anything below.

**A `worker_done` is a claim, not evidence.** Nothing is marked DONE here on a leader's say-so.

---

## Topology in use

Three tiers, per `PROJECT-CLOSURE-PLAN.md` §6.

| Level | Who | Invocation | Notes |
|---|---|---|---|
| 1 · coordinator | Opus, this session | — | owns the graph, gates each batch, does the polling |
| 2 · batch leader | Sonnet 5 | `orca-ide terminal create --command 'claude --model sonnet --dangerously-skip-permissions'` | **never** `worktree create --agent claude` — that silently launches Opus |
| 3 · implementer | DeepSeek V4 Flash | `timeout 420 opencode run --auto --format json -m opencode-go/deepseek-v4-flash "<slice>"` | foreground, synchronous; `--auto` required; `v4.1-flash` is broken |

Verified environment: Orca 1.4.202, repo `18fe34e5-6ebc-4cb9-b9bc-54ea758e94b9`,
`opencode` at `/home/gero/.opencode/bin/opencode`.

---

## Parallelisation reality

`PROJECT-CLOSURE-PLAN.md` §4 caps concurrency below the five-worktree fan-out originally
requested. B0 is the root of B1, B3 and B9, so **nothing runs beside it**. The largest safe
concurrent set after B0 lands is `{B1, B3, B8}` — three. §4 forbids B1‖B2, B3‖B4 and B5‖B2
outright, because B2 consumes B1's contracts and B4 reads B3's result tables.

Track plan agreed with the owner: run B0 alone, then fan out as dependencies land.

| Track | Batches | Unblocks when |
|---|---|---|
| T1 | B0 → B9 | B0 now; B9 after B5, B7, B8 |
| T2 | B1 → B2 → B5 | B0 merged |
| T3 | B3 → B4 | B0 merged |
| T4 | B6 | B3 and B4 merged |
| T5 | B7, B8 | B7 after B2; B8 effectively after B0 |

---

## Guard against a runaway level-3 dispatch

A level-2 leader dispatches `opencode` in the **foreground**, so it is blocked for the whole
call and structurally cannot watch itself. Only the coordinator can. Two failures look
identical from the leader's seat and need opposite handling:

| Class | Signature | Action |
|---|---|---|
| **SPINNING** | CPU rising, `write_bytes` flat — the reasoning loop | Do not kill. Its own `timeout` still has room. Re-probe next cycle; act on two consecutive hits. |
| **ESCAPED** | Age > `timeout` + grace, so SIGTERM was ignored or never sent | Kill `-9`, plus its children — `opencode` spawns them and a bare kill leaves them holding the tty. |

`scripts/reap-stuck-opencode.sh` samples `/proc/<pid>/stat` and `/proc/<pid>/io` twice, 15 s
apart, and classifies every live dispatch. It runs **first** on every 5-minute review cycle.
Exit 0 always — it is a probe, not a gate.

The leader's invocation was hardened to match:

```bash
timeout -k 30 420 opencode run --auto --format json -m opencode-go/deepseek-v4-flash "<brief>"
```

`-k 30` is the part that matters. Plain `timeout` sends SIGTERM only, and a runtime deep in a
reasoning loop can outlive it — the leader's Bash call returns while the process keeps burning
CPU. `-k 30` follows with SIGKILL.

**A dispatch that dies on its timeout means the slice was too wide, not that the model failed.**
Re-dispatch narrower. Re-sending the identical brief with a longer timeout loops again.

---

## A blind spot the 10-minute cycle introduced

A dispatch's ceiling is 480 s. At a 10-minute review cadence, a dispatch can start **and die**
between two probes, so the reaper's "SPINNING for two consecutive cycles" rule can never fire —
by the second probe the process is always gone. That is not theoretical: the first E2E dispatch
timed out mid-write inside exactly that gap, and the only trace was a half-written file the
*leader* noticed before the coordinator did.

Two things follow. A healthy reading never means nothing was reaped in between — cross-check the
dispatch log against `git status` for a slice that ended without an entry. And when the reaper
reports SPINNING, arm a `Monitor` on the pid rather than waiting for a cycle that will arrive
after the outcome is already decided.

---

## DONE

Nothing yet. No batch is closed until the coordinator validates it independently of the
leader's report (§6).

### Coordinator groundwork (verified)

- `PROJECT-CLOSURE-PLAN.md` committed to `main` as `fc57d01`; `.codegraph/` gitignored.
- Worktree `b0-foundations` created on `feat/b0-foundations`, rebased onto `fc57d01` so the
  leader can read the plan.
- Sonnet 5 leader launched and confirmed by banner (`Sonnet 5 with medium effort`), not by
  the send receipt — a receipt proves delivery, never that anything received it.

---

## PENDING

### B0 · Foundations — IN PROGRESS

Worktree `/home/gero/orca/workspaces/plan_cope/b0-foundations`, branch `feat/b0-foundations`.
Leader terminal `term_644ee4dd-c98a-47af-8618-1a1054b7ca1d`.

Unit A — schema:

- [x] 1 · `core.schools.Cue` unique — **build-verified only**. Migration `20260915220905_MakeSchoolsCueUnique`
      with a duplicate-check PL/pgSQL block and a reversible `Down()`. **NOT run against a production
      snapshot** — no Postgres reachable from this worktree. Escalation: needs whoever owns a snapshot
      environment. Not closeable until then.
- [x] 2 · local `008_Schools.sql` — DONE at code level. The offline-first regression it introduced is
      **resolved at the write path**, not papered over: `Schools.EnsureRowAsync` is a single helper called
      from both `LocalRosterRepository.ImportAsync` and `SessionRepository.CreateAsync`. Guarded by
      `CreateAsync_UpsertsSchoolsRow_WhenSchoolHasNoRosterSnapshot`. Local suite 25/25. SQLite cannot `ALTER TABLE ADD FOREIGN KEY`,
      so it uses the mandatory 12-step rebuild. Paired with `SchoolsForeignKeyTests.cs`, which asserts
      `PRAGMA foreign_key_check` empty *and* that a dangling `school_code` actually throws — declared
      FKs that are never enforced would otherwise pass.
- [ ] 3 · `CueCode` at every boundary; normalise on write, never on read. IN FLIGHT. **The write path does
      not normalise at all** — `LocalRosterRepository.ImportAsync` inserts `package.Cue` raw from Central's
      payload, and `LocalRosterPullService.cs:84` guards it with `OrdinalIgnoreCase`, so a cue differing only
      in a normalisable form passes and is persisted raw. The read-side calls are the only thing masking it.
      Mandated order: normalise on write **first**, then remove the reads, then decide explicitly about rows
      already written raw. **Three defects located
      by the coordinator, both must be resolved rather than preserved:** `LocalRosterRepository.cs`
      normalises on READ at lines 210, 236, 270, 298 (defensive double-normalisation that hides whether
      the write path works), and `GeRosterService.cs:372` wraps the single source in a private
      `NormalizeCue` alias — a second name is how a second implementation eventually gets born.

Unit B — tests and CI:

- [x] 4 · `SyncCompat.Tests` — DONE. `ContractToleranceTests.cs` closed the gap: both
      `ignores_unknown_extra_property` (additive tolerance) and `missing_X_becomes_null` (required-field
      presence) now exist. Was: 40 tests across three files, asserting **wire shape**
      (`TryGetProperty(camelCaseName)` on `RootElement`), which is the instrument that actually fails on a
      rename. Clears the ≥15 minimum. **Incomplete**: the plan names three properties and only round-trip
      is covered. Additive-change tolerance and required-field presence are absent — see review log, +57 min.
- [!] 5 · `E2E.Tests` — **builds now, but the test FAILS.** CS0718 resolved. The scenario dies in EF
      model validation: `AnswerKey.CorrectAnswer` is `jsonb` (`ExamEntityConfiguration.cs:73`), which Npgsql
      maps and the InMemory provider does not. **The repo already solved this** — `AuthControllerTests.cs:82-93`
      runs the same `PlanCopeDbContext` on InMemory via `AddSingleton<IModelCustomizer, …>` +
      `UseInternalServiceProvider`. The E2E factory used `ReplaceService<IModelCustomizer, …>` instead, which
      empirically does not take effect. Also a DRY defect: `JsonDocumentFriendlyModelCustomizer` is now defined
      twice, privately, in two test projects. Next wall predicted (unverified): Local's sync services build their
      own `HttpClient` from `central_url` while `WebApplicationFactory` starts no real listener.

Acceptance gates still unverified: `PRAGMA foreign_key_check` empty on a production-shaped
database, migration reversible, rollback rehearsed, `dotnet build PlanCope.slnx -warnaserror`
green, E2E running on `windows-latest`.

Open escalations: none raised yet. Two are expected — duplicate CUEs in production, and
`delivery_sessions.school_code` rows with no matching roster snapshot.

### B1 – B9 — NOT STARTED

Blocked on B0 per §4. See the track table above.

---

## Review log

| Time | Finding |
|---|---|
| launch | Leader started, mandate delivered, turn confirmed started from the rendered screen. |
| +1 min | Healthy. Leader is reading code to decide (rg over schema, DbUp scripts, test shells, CI file) before writing slice briefs — explicitly allowed by its mandate. `pgrep -x opencode` empty, which at this stage is expected, not drift: it has not reached a dispatch yet. No production code written; `git status` shows only the untracked `_briefs/`. Already confirmed from the repo that the `Cue` index exists and is non-unique, matching plan task 1. |
| +6 min | Healthy, no correction issued. Leader verified the level-3 primitive exists before relying on it (`which opencode` → v1.18.31, independently re-probed by the coordinator: exit 0, no hang), then went on reading existing tests (`MigrationDiscoveryTests.cs`) to learn the project's conventions before writing slice briefs. That is the right order — a brief written blind to existing conventions produces output the leader has to throw away. `pgrep -x opencode` = 0 and `_briefs/B0-PROGRESS.md` still absent, both expected: no dispatch has been issued yet. Zero production code written, `git status` still only the untracked `_briefs/`. **Intervening at this point would itself be the error** — the drift signal is a leader writing code, not a leader reading it. |
| +12 min | **Delegation confirmed by positive evidence**, and the runaway guard installed. Leader wrote a slice brief, dispatched with the exact mandated command, and `pgrep -x opencode` returned a live pid in the correct cwd — 3.9 MB written across a 15 s sample, so real work, not a loop. `CoreEntityConfiguration.cs` is modified by the implementer, not the leader. `_briefs/B0-PROGRESS.md` now exists and is honest: it records that no production snapshot is reachable from this worktree, so the migration's "tested against a restored production snapshot" criterion **will be reported as NOT verified rather than silently skipped**. Correction issued (hardening, not drift): `timeout` → `timeout -k 30 420`, bare `opencode run` forbidden outright, and the narrower-not-longer rule for a slice that dies on its clock. Reaper wired as step 1 of the cycle; cron replaced (`a0ef7e96` → `c498a269`). |
| +17 min | Reaper: **healthy** (pid 691002, 232 s, cpu+=346, bytes+=1.5 MB). Hardening confirmed applied — the live dispatch's real argv carries `timeout -k 30 420`, read from `/proc`, not from the leader's claim. Task 1 accepted on evidence (migration read in full, `-warnaserror` clean). **DRY verified clean by the coordinator**: every normalisation in `src/` routes through `CueCode`, zero reimplementations. Three findings sent. The one that matters: **the leader is dictating production code rather than delegating it** — slice A2 hands the implementer the complete `008_Schools.sql` character-for-character, making it author and reviewer of the same bytes. Not reverted: the 12-step rebuild is order-sensitive, the SQL reads correct, and the paired test is genuine independent verification. Rule set for every slice after: **specify behaviour, constraints and acceptance — never bytes**; and if a slice truly needs verbatim SQL, record the reason in the dispatch log so the choice is visible instead of silent. |
| +22 min | Reaper: no live dispatch — correct, A2 returned. **Escalation: the migration introduced an offline-first regression, and I stopped the leader before it patched the symptom.** A2's own test passes, but the full project run went red on `LocalSessionFlowTests` and `LocalRosterRepositoryTests`. I investigated independently rather than waiting for the leader's reading, and the red is correct: `SessionRepository.cs:11` is the only production path inserting into `delivery_sessions`, **nothing in production ever inserts into `schools`** (the only INSERTs are the one-time backfill inside `008` itself), and `LocalSqliteConnectionFactory.cs:15` sets `PRAGMA foreign_keys=ON` on every connection — so the new FK is enforced at runtime, not decorative. Net effect: a freshly activated school whose CUE has no roster snapshot **cannot start its first session offline**. That breaks the §7 Offline gate and §6 rule 3 outright. The five-minute fix — seeding a `schools` row in the test helper — turns the suite green and ships the bug; I forbade it explicitly. Instructed: fix the **write path**, single source, `INSERT OR IGNORE` into `schools` at session creation, decision recorded with reasoning before implementing, then delegated by behaviour not bytes, with acceptance requiring a test that creates a session for a CUE with no roster snapshot. Also flagged: the A2 dispatch-log entry was still unwritten. |
| +27 min | Reaper: **healthy** (pid 705378, 254 s, bytes+=2.6 MB); live argv re-read from `/proc` confirms `timeout -k 30 420`. Leader responded well to all three prior findings: it recorded the byte-dictation justification in the dispatch log exactly as asked, and **independently found a second broken write path I had missed** — `LocalRosterRepository.ImportAsync:60` also inserts without ensuring a `schools` row, not just `SessionRepository`. Task 3 dispatched as a single DRY helper. **New coordinator catch, sent before acceptance:** the leader justified removing the four read-path `CueCode.Normalize` calls with "callers already pass normalised cues". True for reads — I verified `RosterEndpoints.cs:18,38` and `SessionEndpoints.cs:46`. But it never checked the write path, and **the write path does not normalise**: `ImportAsync` persists `package.Cue` raw, past an `OrdinalIgnoreCase` guard. Removing the reads first yields a silent lookup miss — roster on disk, school cannot see it, no error, offline, undiagnosable. Strictly worse than the double-normalisation being removed. Mandated ordering (write, then reads, then an explicit decision on already-raw rows, which `008` propagates into the new parent table) and a test importing a cue that actually needs normalising, since every existing test seeds pre-normalised values. |
| +37 min | Reaper: **healthy** (pid 719817, 888 KB written); live argv carries `timeout -k 30 420`. **The leader refuted my previous finding, and it was right.** I claimed `ImportAsync` persists `package.Cue` raw. It does — but `LocalRosterPackageValidator.cs:19,27-30` is called unconditionally at `LocalRosterRepository.cs:14` and *throws* unless the cue is byte-identical to its own normalised form. A reject-gate, as strong as a coerce-gate here. And `CueCode` is digit-only, so my `OrdinalIgnoreCase` concern was inert — a CUE has no letters to vary case by. The leader traced this instead of complying, recorded the reasoning, and **declined to add a second `Normalize` inside `ImportAsync` because that would have created a second normalisation authority** — the exact DRY violation the plan names. Correct call. I closed its one unproven residual myself: `SessionRepository.CreateAsync` has exactly one caller, `SessionEndpoints.cs:81`, which normalises at line 46; `EmbeddedRosterSeeder.cs:49` passes the same gate. Every write path reaches canonical form. **Unit A accepted on coordinator-run evidence.** Unit B (tasks 4-6) dispatched next, with a warning that a round-trip assertion passes through a renamed field — the contract tests must assert wire shape. |
| +47 min | Reaper: no live dispatch; the task-4 dispatch had already returned and the leader was reading its output. Slice A4 logged and accepted on evidence — the offline-gate guard test exists, local suite 26/26. Unit A complete at code level. **Intervened on durability, not quality.** The leader is 39 min and **61.3k tokens into a single turn** with tasks 4-6 still ahead, and **all of Unit A is still uncommitted**. An Orca terminal does not survive a session restart: the files would live, the reasoning would not — including the validator reject-gate analysis, which is not reconstructible from the diff. Instructed: commit Unit A now as three reviewable work units (Central migration / Local 008 + FK rebuild / CueCode sweep + helper), tests in the commit with the code they cover; and make `B0-PROGRESS.md` self-sufficient on **why**, not just what — the reject-gate reasoning, the fixture-vs-production-bug distinction, and which brief shapes produced usable deepseek output, which is the most valuable and least recoverable thing it knows. Told it to stop at a committed boundary and report honestly if context runs short, rather than push through into a half-finished tree. Agreed with its call that the 11-digit CUE literal is non-blocking; asked for the one-character fix to ride along on the next test slice, since a test seeded with a value production cannot produce will mislead someone eventually. |
| +57 min | Reaper: **healthy** (pid 746353, 2.9 MB). **Checkpoint landed**: three clean conventional commits (`bcfbf9a`, `9bc986e`, `f4c26bf`) split along the seams requested, no AI attribution — Unit A is durable, and the context-loss risk I flagged last cycle is closed. Task 4's tests assert wire shape as warned, 40 tests, minimum cleared. **Gap found in task 4:** the plan names *round-trip, additive-change tolerance, required-field presence*; only round-trip exists. Zero tests mention unknown/extra fields, and every `required` hit is a payload literal, not an assertion. This is the one that matters — Central and Local drift by design, so the day Central emits a DTO with a new field, an older Local client that throws instead of ignoring breaks **every un-updated school on its next sync, offline, at once**. I checked the current behaviour to make the slice precise: `UnmappedMemberHandling` is configured nowhere, so the default `Skip` already tolerates unknown members. The behaviour is correct; the test that **pins** it is missing, and nothing would fail if someone added `Disallow` later. Also applied the new protocol's disjointness rule against itself: this slice edits the same SyncCompat files as the live dispatch, so it **cannot** join the task 5+6 wave. |
| +67 min | Reaper: **healthy** (pid 756800, 1.5 MB). Task 4 gap closed properly — `ContractToleranceTests.cs` has both legs. Task 6 done. `Program.cs` touched, and the diff is the right minimal one: `public partial class Program;`, exactly what `WebApplicationFactory` needs and nothing more — KISS held under a change that invites scope creep. The live dispatch declares its file set explicitly in the brief, so the new protocol is being applied, not just acknowledged. Three items sent. **(1) Durability slip** — the dispatch log stops at A4 while three slices have landed since; a log written retrospectively is a summary, and summaries are what this topology exists to distrust. Told to backfill before the next dispatch, not at batch end. **(2)** CI step still named "…if configured" after `--if-present` was removed — a name that now lies, and a step name is documentation someone reads before the run line. **(3) Handoff finding for B1, explicitly not to be fixed here:** the leader's own test records `LoginResponse` with a missing access token deserialising to null rather than being rejected. Pinning it is right; a hostile auth response deserialising "successfully" into a null token is a hazard for any consumer that does not null-check — but authentication is B1 scope, so it must be inherited, not chased. |
| +77 min | Reaper: **SPINNING** (pid 768568, 353 s, cpu+=486 ticks, **bytes+=0**) — the reasoning-loop signature, first real firing of that classifier. Not killed: 127 s of its own ceiling remained, and killing on a 15 s sample turns a model that is thinking into a false positive. Reading the dispatch's argv surfaced something the reaper alone would not have: **this is the second attempt at the same slice.** The brief says the target file was "written by a previous pass that ran out of time" — so a first E2E dispatch already died on its timeout **inside the 10-minute blind spot**, and the leader caught it before I did. It re-dispatched correctly, narrowing the file set to exactly one path rather than extending the clock. Armed a `Monitor` on the pid instead of waiting for the next cycle, which would have arrived after the outcome was already decided. If this attempt also writes nothing, the slice needs a structural change — a third attempt at the same shape is the loop the protocol forbids. |
| +82 min | Monitor fired: **second consecutive E2E dispatch ended having written zero bytes** (file unchanged at 13 499 bytes). Structural-change trigger met. **Diagnosed in three seconds with a build the leader never ran:** `PublishPullRunPushTests.cs(223,26) CS0718 — LocalApiApplication: static types cannot be used as type arguments`. The factory inherits `WebApplicationFactory<LocalApiApplication>` and that class is `public static`. The implementer was not weak; **the task was impossible as specified**, so it reasoned until the clock ran out, twice, doing exactly what it was asked. The general lesson sent to the leader: **when a dispatch returns nothing, build before re-dispatching — diagnosis is leader work.** A 3-second build would have saved two 7-minute timeouts. Also warned that the obvious fix, `WebApplicationFactory<Program>`, will hit an ambiguity: `Program` is declared in the **global** namespace by both `PlanCope.Local.Api/Program.cs` (pre-existing) and `PlanCope.Central.Api/Program.cs` (added this batch), and this test references both assemblies — flagged as predicted, not observed, since the build stopped at CS0718 first. Suggested a dedicated non-static marker per assembly, left the choice to the leader, and required it to re-dispatch **surgically** — naming the type and the line — because "finish this 296-line file" is a brief with no edges, which is why it produced nothing twice. |
| +92 min | Reaper: no live dispatch. CS0718 fixed, **E2E compiles**. Ran the test myself rather than accepting a build-green report — **it fails**, in EF model validation, before the scenario executes: `AnswerKey.CorrectAnswer` is `jsonb` and the InMemory provider cannot map `JsonDocument`. **The important part is that this repo had already solved it**: `AuthControllerTests.cs:82-93` runs the very same `PlanCopeDbContext` on InMemory using `AddSingleton<IModelCustomizer, JsonDocumentFriendlyModelCustomizer>` on an internal service provider. The slice reinvented it with `ReplaceService<…>`, which reads equivalent and empirically is not. That is §6 rule 1 — never assume something does not exist — violated thirty lines away in a sibling test project, and it produced a **DRY defect too**: the customiser is now declared twice, privately, in two files. Instruction to the leader generalises it: when a slice needs infrastructure, name the existing file that already does it and tell the implementer to copy that pattern. Also drew a boundary — if the predicted HTTP-transport wall appears next, **stop and report**; binding a real socket between Local and Central in tests is an infrastructure decision reserved to the coordinator. |

---

## Session close — 2026-09-15

Coordinator polling stopped (cron `864af999` deleted). The B0 leader terminal
`term_644ee4dd-c98a-47af-8618-1a1054b7ca1d` is **still alive and still owns the batch** — it was
not stopped, because killing in-flight work is the owner's call.

**Committed and durable** on `feat/b0-foundations`: `bcfbf9a`, `9bc986e`, `f4c26bf` — plan tasks
1, 2 and 3. Unit A survives a session restart.

**Uncommitted in the working tree**, real work that would survive on disk but is unattributed:
tasks 4 and 6 (the SyncCompat contract suite including `ContractToleranceTests.cs`, and the
`ci-local-app.yml` no-op removal) plus the failing E2E scaffold. **First action on resume: commit
tasks 4 and 6 as their own work units.** They are done and green; only task 5 is not.

**Open, and not closeable by any agent here:**

1. Task 1's migration has never run against a restored production snapshot — no Postgres is
   reachable from this worktree. Needs whoever owns a snapshot environment.
2. Task 5 fails. Fix is known and named above. If the predicted HTTP-transport wall follows,
   it is an infrastructure decision for the coordinator, not the leader.
3. `LoginResponse` with a missing access token deserialises to `null` rather than being
   rejected — pinned by test, **carried to B1**, deliberately not fixed in B0.
4. The CI step is still named "Test ClientApp (Vitest, if configured)" after `--if-present`
   was removed.

**Carried forward for B1:** `scripts/LEVEL3-DISPATCH-PROTOCOL.md` (fan out disjoint slices —
three concurrent dispatches measured at 67 s wall against ~200 s serial) and
`scripts/reap-stuck-opencode.sh`. Two lessons this batch paid for in full: **build before
re-dispatching**, since two 7-minute timeouts were one compile error the leader never looked
for; and **name the existing file** when a slice needs infrastructure, since a working solution
thirty lines away in a sibling project was reinvented worse.

---

## Resumed — B0 is code-complete, verified by the coordinator

Ran independently, not taken from the leader's report:

- `dotnet test PlanCope.slnx` → **122 passed, 0 failed**, across all seven test assemblies,
  including the E2E scenario (1/1) and SyncCompat at 46.
- `dotnet build PlanCope.slnx -warnaserror` → **0 warnings, 0 errors**.
- CI step renamed to "Test ClientApp (Vitest)" — the name no longer contradicts the run line.

**Task 5 solved without crossing the boundary.** The leader confirmed the prediction that
`WebApplicationFactory` never starts a real listener, and instead of building the real Kestrel
binding it was not authorised to build, it pointed Local's two named HTTP clients at Central's
in-memory TestServer handler. No socket, no port, no production code touched — and it escalated
rather than deciding.

**The duplication was worse than reported, and the fix changed direction because of it.** The
leader offered the two `JsonDocumentFriendlyModelCustomizer` copies as "different internal
approach, same rule". Reading both shows they are **not equivalent**: the E2E copy sets the
converter through the fluent builder because — per its own comment — the convention-level API
does not survive model finalisation once `HasColumnType("jsonb")` is configured on the property.
The Central copy uses exactly that convention-level path, and passes today only because nothing
in `AuthControllerTests` exercises a jsonb property hard enough to expose it.

So one copy is **latently broken**, which is worse than two identical copies: it will look fine
until someone adds a jsonb-backed assertion to the Central suite, and then fail for a reason
nobody connects to this. Authorised: one shared source file carrying the **fluent-builder**
implementation, linked into both csprojs via `<Compile Include>`. No new project — that is the
limit of what was approved.

**Remaining to close B0:** commit tasks 4, 5 and 6 as work units (still untracked), then the
unification as a separate commit, re-verifying Central stays at 30/30.

**Still not closeable by any agent:** task 1's production-snapshot verification.

---

# B0 CLOSED — merged to `main` as `c36ae67`

Verified by the coordinator, not from the leader's report: **122 tests pass across all seven
assemblies** (E2E 1/1, SyncCompat 46, Central 30, Local 26), and
`dotnet build PlanCope.slnx -warnaserror` is **0 warnings, 0 errors** on the merged tree.

Six work-unit commits, conventional, no AI attribution: `bcfbf9a` Central unique CUE ·
`9bc986e` Local schools table · `f4c26bf` CUE-on-write + ensure-schools-row · `23f6765`
SyncCompat contract suite · `e84853e` E2E publish-pull-run-push · `217442f` CI no-op removal ·
`dab2ad1` shared model customiser · `2f592c2` briefs and dispatch log.

**One item deliberately left open, and no agent here can close it:** task 1's migration has never
run against a restored production snapshot — no Postgres is reachable from these worktrees. It
needs whoever owns a snapshot environment.

---

# Parallel wave launched — B1 ‖ B3 ‖ B8

The largest concurrent set §4 allows after B0. §4 forbids B1‖B2, B3‖B4 and B5‖B2, so these three
are the ceiling, not a choice.

| Batch | Worktree | Terminal |
|---|---|---|
| B1 · activation keys | `b1-activation-keys` | `term_ebf7c8c4-25c5-4af8-a587-1205a6d5b80d` |
| B3 · grading engine | `b3-grading-engine` | `term_ab81b55d-a40a-4d59-82ad-41920f0d28c3` |
| B8 · performance | `b8-performance` | `term_9c2e7b10-439a-493e-b822-e5a14272d530` |

All three rebased onto `c36ae67`, Sonnet 5 confirmed from the rendered banner (not from the send
receipt — a receipt proves delivery, never that anything received it). Review cycle back to
**5 minutes** (`2be45a20`).

**Their mandates ship with B0's lessons already in them**, so nothing is re-learned at cost:
fan out disjoint slices in waves rather than one at a time; build before re-dispatching, because
two 7-minute timeouts in B0 were one compile error nobody looked for; name the existing file when
a slice needs infrastructure; a green build is not a green test; and duplication where one copy
differs subtly is worse than two identical copies.

**What only the coordinator can watch:** B1, B3 and B8 are supposed to touch disjoint trees. If
two of them start editing the same file, that is a plan-level collision, not a leader error.

## Wave review log

| Cycle | Finding |
|---|---|
| wave +9 min | Reaper: **four healthy dispatches, zero stuck** — B1 ×1, **B8 ×3 concurrent**. The fan-out protocol worked on first use: B8 ran three simultaneous slices where B0 would have run them one after another. B1's wave file is named `w0-b…`, so it is waving too. No commits yet in any worktree; expected this early. **B3 spent 135 k tokens and 8 min on a read-only fork survey before its first dispatch.** Judged legitimate, not drift: it produced ground truth with file:line citations, confirmed `scoringPolicy` exists nowhere yet before designing against §2.7, and is now writing **two disjoint** Wave 1 briefs. Correcting thoroughness here would repeat the mistake I avoided in B0 cycle 2. Sent a cost note instead — one upfront fork, never one per wave, since a fork inherits full parent context. Gates restated per batch: B3 owns the grading single-source (§7 determinism proven by **shared** golden files, and a formula implemented once per side fails the batch even if both sides pass) and the no-implicit-grading rule (**a sensible-looking default policy is the failure mode, not a missing one**). B1 received the inherited B0 finding — `LoginResponse` with a missing access token deserialises to null — with the instruction to decide explicitly rather than silently inherit it, plus the two-level revocation distinction and the reminder that its DTOs are a published surface B2 consumes. |

**Caveat on the reaper, noted honestly:** `write_bytes` counts *all* process I/O, including opencode's own session DB — so rising bytes proves the process is alive and working, not that it is writing to the worktree. It still separates a spinning model (zero bytes) from a working one; it does not prove code was produced. `git status` remains the only proof of that.
