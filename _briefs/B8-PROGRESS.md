# B8 · Performance — progress

Leader: this session (Sonnet, level-2). All implementation dispatched to level-3
(opencode / deepseek-v4-flash) per `scripts/LEVEL3-DISPATCH-PROTOCOL.md`. No production code
written by the leader; two infra/measurement judgement calls below were the leader's/
coordinator's, not an implementer's.

## Wave 1 — 3 slices, 420s each — all 3 timed out

Briefs were too broad (2-4 tasks each) for the fixed 420s dispatch window; deepseek spent the
whole budget exploring before writing anything. Diagnosed via `git status` + log tool-call
histogram (all `read`/`bash`, zero `write`/`edit` for two of three) before re-dispatching —
not a compile error, a scope problem. One slice (measurement) had already produced real,
useful work (`Argon2idBenchmarkTests.cs`, a genuine benchmark against the production decrypt
path) before being killed — kept and continued rather than redone.

## Wave 2 — 7 narrower slices, disjoint file sets, 420s each — 5 completed, 2 timed out

| Slice | Files | Result |
|---|---|---|
| fe-cleanup | package.json, package-lock.json, ClientApp package-lock.json (deleted), WorkspaceModeTabs.tsx (deleted), vite.config.ts, check-bundle-budget.mjs, ci-local-app.yml | DONE |
| fe-virtualize | QuestionNav.tsx, ExamBlock.tsx, ExamTakingPanel.tsx, new test | DONE (rc=124 but had already finished — see below) |
| fe-polling | useDeliverySession.ts, new test | DONE |
| be-aot | Local.Api.csproj, Local.Host.csproj | rc=124, no file changes — decisive findings were already in the log; continued with a report-only slice (wave 2b) |
| be-sqlite | LocalSqliteConnectionFactory.cs, 009_PerformanceIndexes.sql | DONE |
| measure-argon2 | Argon2idBenchmarkTests.cs, README.md (Argon2id section) | DONE |
| measure-profile | docs/reference-profile.md | DONE |

**fe-virtualize** hit the 420s wall but `git diff` + the log showed it had already run
`npm run test` (17/17 pass) and `npx tsc --noEmit` (clean) and was mid-way through writing its
final text report when killed — the actual work was complete. Verified independently rather
than trusted: re-ran both commands myself, same result.

**be-aot** hit the wall with zero files written, but the log showed real experiments already
run: `dotnet publish -r linux-x64 --self-contained -p:PublishReadyToRun=true` succeeded (exit
0); `-p:PublishTrimmed=true` produced a binary that crashes at startup —
`InvalidOperationException: Must add values for the following parameters: @Id` inside
Dapper's reflection-based parameter binding (`LocalExamRepository.GetByIdAsync` →
`LocalDemoExamSeeder.SeedDemoExamIfMissing`), confirming Dapper is not trim-safe for this
codebase's real query shapes — empirically, not assumed. Dispatched a narrow report-only
continuation (wave 2b) rather than re-running the experiments.

## Wave 2b — 1 slice, report-only

`be-aot-report`: no further csproj changes (correct outcome — ambiguous/Windows-only result).
Confirmed `dotnet build PlanCope.slnx -warnaserror` green. Recommendation recorded: do not
adopt `PublishTrimmed` for `PlanCope.Local.Api`; R2R measured viable on linux-x64 as a proxy
but the real target is win-x64/WebView2 and needs verification on that platform before
adoption — not flipped on blind. `PlanCope.Local.Host.csproj` untouched: it requires Windows
to build, so R2R/trimming could not be evaluated for it in this environment.

## Task-by-task status (plan §5, B8)

1. **Delete unused deps + orphaned components** — DONE. `@dnd-kit/core`, `@dnd-kit/sortable`,
   `@dnd-kit/utilities`, `react-hook-form`, `@hookform/resolvers`, `zod`, `@base-ui/react`
   removed (verified zero imports before removal). `WorkspaceModeTabs.tsx` deleted (verified
   unreferenced). `UpdateStatus.tsx` **left untouched** — cross-batch coordination point with
   B7, not resolved here; do not delete without confirming B7's status first.
2. **Vite manualChunks, build.target, CI budget** — DONE. Vite 8 here is Rolldown-based and
   dropped Rollup's `build.manualChunks`; used the supported equivalent
   (`rollupOptions.output.codeSplitting.groups`) — documented in a comment. `target: "chrome120"`
   chosen for the WebView2 Evergreen floor. Budget script measured the real post-cleanup
   `dist/` (250,159 bytes) and was **corrected by the coordinator** from an unexplained
   300,000-byte default to 350,000 bytes (≈40% headroom, documented in the script) — a budget
   pinned near today's size breaks on the first legitimate feature and gets deleted under
   deadline pressure instead of examined. CI step confirmed purely additive: `git diff` on
   `ci-local-app.yml` shows only two new lines appended after the existing "Build ClientApp"
   step; the line-46 area B0 fixed is untouched.
3. **Virtualize long lists** — DONE for the two lists named in the plan (`QuestionNav.tsx`,
   the `.student-questions` list in `ExamTakingPanel.tsx`). Hand-rolled windowing (no new
   dependency — deliberate KISS call, not an oversight): every item keeps a mounted wrapper
   with a reserved height so `scrollIntoView`-based jump-to-question and scroll position stay
   stable; only near-viewport items render real content, everything else is a placeholder.
   `ExamBlock.tsx`'s `id={block.id}` moved to the wrapper `<div>` in `ExamTakingPanel.tsx`
   (verified no other code relied on the `<section>` carrying that id). 7 new tests cover:
   wrapper-always-mounted, placeholder-vs-rendered by distance, nav-jump-to-offscreen,
   placeholder-to-content swap, answer survival across scroll-away-and-back, and the window
   math itself. `npm run test` 17/17 pass, `tsc --noEmit` clean — verified independently.
4. **Adaptive polling** — DONE. Replaced the unconditional 3s `setInterval` with
   `createProgressPoller`: 3s while visible and progress is changing; backs off (doubling,
   capped 30s) on unchanged progress or `document.visibilityState !== 'visible'`; resets to
   3s immediately on a progress change or on becoming visible again; stops entirely (no
   in-flight fetch) when there's no active session. 4 new tests with fake timers cover all
   four behaviours.
5. **ReadyToRun / trimming evaluation** — PARTIAL, correctly reported as such. R2R: succeeds
   on linux-x64 (proxy measurement only — real target win-x64 needs a Windows machine, not
   available here). Trimming: **fails** — Dapper reflection breaks at runtime, confirmed by
   running the trimmed binary, not assumed. No csproj changes made; this is the correct
   outcome per the plan's own risk note ("verify against the full test suite, not a smoke
   test" — an ambiguous/failing result means don't ship the change). **Still needed before
   this task can close:** a real win-x64 R2R measurement on Windows CI or hardware.
6. **SQLite indexes/pragmas** — PARTIAL, correctly scoped. Pragmas: added `mmap_size`
   (64 MiB) and raised `cache_size` to 8 MiB, both commented with the low-memory reasoning.
   Indexes: B4 (local statistics/rollups) **does not exist yet on this branch** — verified,
   not assumed (no `*Rollup*`/`*Statistic*` file under `src/Local`). Added one index
   (`ix_student_attempts_session_local_sequence`) for a real existing query
   (`AttemptRepository`'s `MAX(local_sequence)` scoped by session) rather than inventing
   speculative B4 indexes. **The plan's literal ask — "covering indexes for the B4 rollup
   queries" — is blocked pending B4 landing.** Revisit once B4 merges.
7. **Benchmark Argon2id on real hardware** — PARTIAL, correctly reported as such. Measured on
   this dev machine only (16-core, not the 2-core/4GB/HDD reference target) via a real
   benchmark against the production `EnvelopeDecryption.DecryptCueAsync` path: ~203-277 ms at
   the chosen 64 MiB/3/1 (re-run twice, both in the 200-280ms band — some variance under
   shared-machine load, both far from the 198ms original figure's implied precision).
   Recommendation recorded in README: keep parameters, do not weaken without owner sign-off.
   **Real school hardware still not available — this is the named escalation point, not
   resolved, only better-documented.**
8. **Reference profile** — DONE as a specification. `docs/reference-profile.md`: hardware
   profile (2-core/4GB/HDD), budgets for cold start (≤15s target/≤25s ceiling), idle memory
   (≤1.0GB target/≤1.5GB ceiling, whole WebView2 process tree), and 100-question render/scroll
   (≤300ms first paint, 0 dropped frames >50ms, tied explicitly to task 3's virtualization
   contract), plus exact verification procedures (stopwatch trace points, WPR/dotnet-trace,
   DevTools 6× CPU throttle script). Explicitly, repeatedly flagged as **budgets, not
   measurements** — no reference hardware was available to run any of it.

## Verification run by the leader (not taken on any dispatch's word)

- `npm run test` (ClientApp): 4 files, 17/17 pass.
- `npx tsc --noEmit` (ClientApp): clean.
- `npm run build --workspace plancope-local-host-ui`: succeeds, `dist/` = 250,159 bytes.
- `node scripts/check-bundle-budget.mjs`: OK against the corrected 350,000-byte budget.
- `dotnet build PlanCope.slnx -warnaserror`: green (includes the cross-targeted
  `net8.0-windows` Host project via `EnableWindowsTargeting`).
- `dotnet test tests/PlanCope.Local.Api.Tests`: 26/26 pass.
- `dotnet test tools/PlanCope.RosterCrypto.Tests`: 6/6 pass, including the Argon2id benchmark.
- `git diff` reviewed independently, file by file, before accepting any slice.

## What was substituted for the real thing, and what that does NOT prove

Per the owner's standing rule: a green local suite proves the code works against the
substitutes chosen on this machine, nothing more. Named per task:

1. **Dependency cleanup / orphan deletion.** Verified: `npm run test`, `npm run build`, `tsc
   --noEmit`, all on this Linux dev machine's Node install. NOT proven: that the packaged
   WinForms + WebView2 host actually loads the rebuilt `dist/` correctly — that only happens
   inside a real Windows build/install, which this environment cannot produce. CI's
   `ci-local-app.yml` build step is closer to real but still isn't a packaged install.
2. **Vite build tuning + bundle budget.** Verified: local `npm run build`, `dist/` =
   250,159 bytes, budget script passes at 350,000. NOT proven: that CI's pinned Node 24
   runner produces byte-identical output — it should, given the lockfile, but that is an
   assumption until CI actually runs the new step.
3. **List virtualization.** Verified: vitest + jsdom, 7 new tests, `IntersectionObserver`/
   scroll behaviour mocked. This proves the windowing *logic* (correct start/end bounds,
   answers surviving unmount/remount, wrapper ids stable). It does **NOT** prove there is no
   jank in a real browser — jsdom does no layout or paint. The only real proof is
   `docs/reference-profile.md` §4.6 (DevTools 6× CPU throttle, real frame timing), which has
   not been run.
4. **Adaptive polling.** Verified: vitest fake timers, 4 tests. Proves the state machine
   (backoff, reset, visibility gating) is logically correct. Does NOT prove real
   `document.visibilitychange` behaviour across actual OS-level tab/window switching, or
   real network latency effects on the backoff schedule.
5. **R2R / trimming.** Verified: `dotnet publish` on **linux-x64** as a stand-in for the real
   win-x64 + WinForms + WebView2 target, which cannot build in this environment. The
   trimming failure (Dapper reflection) is a real, reproduced crash and very likely transfers
   to win-x64 since it's the same IL/reflection behaviour — but the R2R *success* on
   linux-x64 says nothing about WinForms/WebView2-specific R2R compatibility, which is the
   part the plan actually asked about ("AOT is likely unavailable under WinForms + WebView2").
   This still needs a real win-x64 run, ideally in CI (`windows-latest` is already used
   elsewhere in this repo's workflows).
6. **SQLite pragmas / index.** Verified: `dotnet test` against temp SQLite files on this
   machine's filesystem (SSD-backed, not the reference profile's 5400rpm HDD, and not
   memory-constrained). This proves the index is used and the pragmas don't break anything;
   it proves nothing about `mmap_size`/`cache_size` behaviour under real HDD random-I/O or a
   genuinely memory-pressured 4GB machine.
7. **Argon2id benchmark.** Explicit already, restated: 203-277 ms is this 16-core dev
   machine's number, not the 2-core/4GB/HDD reference profile's. Do not read it as a
   field measurement.
8. **Reference profile document.** Contains zero measurements by design — every number in it
   is a target, not a result. Says so in its own §5.

## Not done / escalations for Opus

- **B7 coordination on `UpdateStatus.tsx`.** Left untouched per the plan's explicit
  instruction. Needs a real check against B7's current state before it can be deleted or
  kept.
- **Argon2id re-parameterisation is a live, unresolved security trade-off**, not just a
  measurement gap. Current recommendation is to hold, but no real low-end hardware number
  exists. Owner sign-off required if this ever needs to change (plan §6 risk list).
- **R2R for win-x64 / the WinForms Host project** needs a Windows environment to actually
  verify; only a linux-x64 proxy measurement exists.
- **B4-scoped SQLite indexes** are blocked until B4 lands — flagged, not silently dropped.
- **Reference-profile budgets are unverified targets**, not measurements — no hardware
  available in this environment. Needs a pass on real 2-core/4GB/HDD hardware or Windows CI
  before the batch can be considered fully closed on this point.
