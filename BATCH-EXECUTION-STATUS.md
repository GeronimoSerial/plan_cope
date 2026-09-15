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

- [ ] 4 · `tests/PlanCope.SyncCompat.Tests` — xunit + ≥15 real contract tests
- [ ] 5 · `tests/PlanCope.E2E.Tests` — xunit + ≥1 green end-to-end scenario
- [ ] 6 · `ci-local-app.yml:46` — remove the `npm run test --if-present` silent no-op

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
