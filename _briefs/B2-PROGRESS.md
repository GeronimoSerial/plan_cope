# B2 — Node enrolment and hardware identity: progress log

## Resumption after runtime restart (2026-09-16)

A prior leader session was killed by an Orca runtime restart before any frontend slice
landed; this document (written by that session, see below the line) survived and is what
this resumption was recovered from. Recovery sequence: re-read `_briefs/B2-LEADER.md`, this
file, `scripts/LEVEL3-DISPATCH-PROTOCOL.md`. `docs/DELEGATION-RULES.md`, which the resumption
prompt named, **does not exist in this repo** — proceeded on `B2-LEADER.md` and the dispatch
protocol alone, which were sufficient. Fast-forwarded the branch 22 commits onto
`origin/feat/b2-node-enrolment` (B3/B4's merged data layer, per the coordinator's PR #27
rebase) before doing anything else. Migration `012_NodeIdentity.sql` still slots correctly
after the merge — no third collision.

**Wave 1 (this session): Phase A frontend, `ActivationKeyStore` wired live, task 3 retired.**
Commits `ea9b232`, `9355b04`, `16b779f`. Dispatched as 3 disjoint slices (`MainForm.cs`;
`docs/roster-release.md` + delete `Build-SchoolRelease.ps1`; `ActivationScreen.tsx` +
`HostApp.tsx`), then a 4th narrow slice for the test file once the first attempt at bundling
component+test together proved too wide. Two dispatch failures worth recording for the next
wave: (1) the first `ActivationScreen.tsx` slice burned its whole 420s budget on a confused
`npm install` inside `ClientApp/`, which is an npm-workspace member with no `node_modules` of
its own by design — deps hoist to the repo root; (2) the retry burned its budget reading a
giant minified vendor JS bundle instead of source. Both fixed by narrowing the brief (drop the
test file into its own slice) and adding an explicit "do not read node_modules/dist" rule —
third attempt landed clean. **Diagnosis, not re-dispatching blind, is what fixed it — matches
lesson 1 in `B2-LEADER.md`.**

- `MainForm.cs`: `isActivated` now comes from a real `GET /api/activation/status` call
  (`_phaseAComplete`, refreshed at API startup and after each activation attempt), not
  `ActivationKeyStore.HasStoredKey`. The dead `host:activate` bridge message and `Activate()`
  method are removed. Two new bridge messages: `host:getStoredPassphrase` (host responds with
  `ActivationKeyStore.Load()`'s decrypted contents, or null) and `host:activationComplete`
  (host re-verifies against the API — never trusts the renderer's say-so — then calls
  `ActivationKeyStore.Store()` and re-broadcasts `host:context`). **Task 6 done**: `Load()` is
  now reachable at runtime for the first time. Design call made here, not in the plan text:
  the passphrase is stored so it can pre-fill the field on a later Phase A re-run (DB reset,
  troubleshooting) — re-activation (task 8, unstarted) is about the Phase B *activation key*,
  not this passphrase, so this does not double as task 8's mechanism. Flagging this as a
  judgment call for the coordinator to confirm, not a plan-mandated design.
- `ActivationScreen.tsx`: real Phase A flow — fetches `/api/activation/bundle-cues`, renders a
  CUE selector, posts `/api/activation/unlock`, shows the API's own Spanish error message on
  400. **An operator can now complete Phase A through the actual application** — this closes
  the single biggest gap the previous session flagged. **NOT verified**: interactive
  fetch/bridge behaviour has no automated test. This codebase's component tests are
  `renderToStaticMarkup` snapshots only — no React Testing Library, no fetch mocking, no
  simulated clicks are installed anywhere in this project. Added one more static-render
  assertion (the pre-effect loading state) in the same convention; did not add a testing
  library to test the rest, since that is a new-dependency call reserved for the coordinator
  per `B2-LEADER.md`'s escalation rule, not something to add unilaterally mid-slice.
- `Build-SchoolRelease.ps1` retired (task 3 fully done, not just backend). Confirmed via
  `rg RosterBundlePath src/Local/PlanCope.Local.Host/PlanCope.Local.Host.csproj` (no match)
  and reading `.github/workflows/release.yml`'s `host` job that the script was never wired
  into CI — that job already does its own universal `dotnet publish`, unrelated to this
  script, so retiring it was pure deletion with no pipeline change. `docs/roster-release.md`
  rewritten to describe the actual current flow (one encrypted bundle, one build, CUE chosen
  at Phase A unlock); every claim in the rewrite was checked against the real code
  (`RosterBundleOptions.SectionName`, `RosterCrypto`'s `pack` CLI flags,
  `RosterReleaseTool`'s CLI shape, `DocumentHmacService`) before being written, not assumed
  from the old doc.
- One process note: the `MainForm.cs` commit (`ea9b232`) picked up
  `Build-SchoolRelease.ps1`'s deletion because the dispatched agent for that slice had already
  `git rm`'d it (staged) before I ran `git add` for the unrelated file — the two are
  independent, disjoint changes that happened to land in one commit. Not undone (the deletion
  was already independently reviewed and correct); the docs update for the same task landed
  in its own commit (`9355b04`) with accurate attribution. Worth watching for on future waves:
  check `git status` before `git add <specific-file>`, not just before `git commit`.

Both waves built green (`dotnet build PlanCope.slnx -warnaserror`, 0/0) and the ClientApp
workspace passed independently (`npm run build` + `npm test`, 18/18) before each commit.

## Status: PARTIAL — no RevocationEnforcer, no re-activation screen, no Phase B UI yet. Do not read any task below as "done" without reading its status line.

Of the plan's 8 tasks under "### B2 ·" in `PROJECT-CLOSURE-PLAN.md`, as of this resumption's
Wave 1: **tasks 1, 3 and 6 are fully done; task 2 (Phase A) is done including its UI; task 5 is
backend-done as before. Task 4 (Phase B UI) is next (Wave 2). Tasks 7 (`RevocationEnforcer`)
and 8 (re-activation screen) have no code at all.** A reviewer should still read this document
task-by-task before looking at the diff — several tasks look further along in code than they
are in the actual product.

This batch was run as a level-2 leader dispatching to `opencode`/DeepSeek-V4-Flash per
`scripts/LEVEL3-DISPATCH-PROTOCOL.md`. Every production line below was written by a dispatched
implementer, reviewed and independently re-verified (build + test, not trusted on the
implementer's own report) by the leader before being committed. The leader wrote no production
code — only the DI/routing wiring that connects already-reviewed pieces together (see "Wave 4"
below), which is glue, not logic.

## Status by plan task (read this before the diff)

1. **`HardwareFingerprintService`** — **DONE**, backend-complete. Composite hash + persisted
   per-component match data, in exactly one place (`src/Local/PlanCope.Local.Api/Services/HardwareFingerprintService.cs`),
   consumed correctly by both Phase A (`ActivationEndpoints`) and Phase B (`EnrolmentEndpoints`).
   Tested with a fake `IRawHardwareSignalReader` (7 tests): determinism, per-component change
   sensitivity, null-vs-empty-string distinction, components JSON always names all three signals.
   **NOT verified**: the real `WindowsHardwareSignalReader` (registry `MachineGuid`/CPU string,
   `GetVolumeInformationW` P/Invoke) has never executed — this machine is Linux. It compiles
   (confirmed: `dotnet build PlanCope.slnx -warnaserror` is green including this file) but every
   claim about what it actually reads on real Windows hardware is unverified. Fingerprint
   stability across reboot/Velopack-update/RAM-change (an explicit acceptance criterion) is
   **entirely unproven** — it requires a real Windows machine, which does not exist in this
   environment.

2. **Phase A rewrite (passphrase → Argon2id → real DEK unwrap → CUE selection → `schools` row)**
   — **DONE, including the UI, as of Wave 1 (2026-09-16)**. Backend unchanged from the prior
   session: `ActivationEndpoints.cs` calls the real crypto path
   (`EnvelopeDecryption.DecryptCueAsync`, genuine Argon2id + AES-GCM). `ActivationScreen.tsx`
   was rewritten this wave to call `/api/activation/bundle-cues` and `/api/activation/unlock`
   directly, and `MainForm.cs` now sources `isActivated` from a real
   `GET /api/activation/status` call. **An operator can now complete Phase A through the
   actual application.** **Still NOT verified**: this was never exercised end-to-end on real
   Windows hardware with WebView2 actually running — verification here is `dotnet build`
   (green) + the ClientApp's own build/test (green, 18/18) + independent diff review, not a
   live run. The acceptance criterion "Phase A completes with the network cable unplugged, and
   a full exam session runs to submission afterwards" still needs a real device to confirm.

3. **Remove the single-CUE build restriction / retire `Build-SchoolRelease.ps1`** — **DONE**,
   as of Wave 1 (2026-09-16). Backend half unchanged from the prior session
   (`RosterBundleOptions.Cue` removed, CUE is now a runtime parameter).
   `scripts/Build-SchoolRelease.ps1` is deleted; `docs/roster-release.md` rewritten to describe
   the actual current flow. Confirmed before deleting: `.github/workflows/release.yml`'s `host`
   job does its own universal `dotnet publish` and never referenced this script, so retiring it
   required no CI change.

4. **Phase B enrolment screen + `POST /api/activation/redeem`** — **PARTIAL, backend only**.
   `src/Local/PlanCope.Local.Api/Endpoints/EnrolmentEndpoints.cs` (`POST /api/enrolment/redeem`)
   builds a real `ActivationRedeemRequest` (B1's published contract, unmodified — no parallel
   contract invented) from the fingerprint + the operator-supplied activation key, posts it to
   Central, and on success writes `sync_state`'s `node_id`/token keys and flips
   `node_identity.credential_state` to `"active"`. **What does NOT exist**: no
   `EnrolmentScreen.tsx`, no client-side activation-key checksum validation, no wiring into
   `HostApp.tsx`. **An operator cannot enrol a node through the actual application today** — the
   endpoint is real and reachable by an HTTP client, but nothing in the shipped product calls it.

5. **Credential refresh (401 detected once, not per-service)** — **DONE for what was specified**.
   `NodeCredentialRefresher` + `CentralCredentialHandler`
   (`src/Local/PlanCope.Local.Api/Services/`) give all three existing sync services
   (`LocalExamPullService`, `LocalRosterPullService`, `LocalOutboxPushService`) plus
   `EnrolmentEndpoints`'s own client ONE shared 401-detect → refresh-once → retry-once path,
   via `IHttpClientFactory`'s `AddHttpMessageHandler`, instead of three independent copies. The
   duplicated manual Bearer-header code was deleted from all three services. `NodeRevoked = true`
   or an outright failed refresh both flip `node_identity.credential_state` to `"revoked"` — this
   is the only revocation-detection signal this batch built, deliberately: detection at large
   (a background poll, a proactive check) is B5's job per the plan, not B2's, and building a
   second detection path here would be exactly the duplication §6 forbids. Tested for real against
   a `DelegatingHandler` chain with a fake inner handler (not mocked at the method level) —
   attach-token, 401→refresh→retry-with-new-token, and no-retry-when-refresh-has-nothing-to-work-with
   are all covered (`CentralCredentialHandlerTests.cs`, `NodeCredentialRefresherTests.cs`).
   **NOT verified**: against a live Central instance — every test fakes the HTTP layer, because no
   reachable Central exists in this environment. The redeem endpoint itself
   (`EnrolmentEndpointsTests.cs`) has an explicit, honest `[Fact(Skip = "...")]` rather than a
   fabricated pass: no `WebApplicationFactory`-style fixture exists yet in this test project for
   Local.Api endpoints that fakes an outbound Central call, and building one was judged out of
   scope for the time remaining.

6. **`ActivationKeyStore.Load()` made load-bearing** — **DONE**, as of Wave 1 (2026-09-16).
   `MainForm.cs`'s `host:getStoredPassphrase` handler calls `Load()` and returns the decrypted
   passphrase to `ActivationScreen.tsx`, which pre-fills the passphrase field if present.
   `Store()` is called from the new `host:activationComplete` handler after a real, verified
   Phase A success. Design call made without a plan citation (the plan only says "make it
   load-bearing"): this ties `Load()` to re-running Phase A conveniently, not to task 8's
   re-activation (which recovers from a revoked *activation key*, a different credential) —
   flagged for the coordinator to confirm this reading is the intended one.

7. **`RevocationEnforcer`** (wait for session end → drain outbox → wipe → lock, resumable via
   `node_identity.revocation_stage`) — **NOT STARTED**. No design work beyond the schema column
   (`node_identity.revocation_stage`, added in the merged `012_NodeIdentity.sql` migration) exists.
   Nothing in this batch executes any part of the fixed sequence in plan §2.8.

8. **Re-activation screen for a locked node** — **NOT STARTED**.

## Cross-cutting gates from the batch mandate (`_briefs/B2-LEADER.md`) — checked honestly

- **Offline-first (Phase A fully offline)**: crypto, persistence AND now the UI path are real.
  **Met as far as this environment can verify** — never exercised on real Windows hardware
  with WebView2 actually running (see below).
- **Two-phase separation**: respected structurally — Phase A (`ActivationEndpoints`) and Phase B
  (`EnrolmentEndpoints`) are separate endpoint groups, separate concerns, and Phase B's redeem call
  requires a `node_identity` row that only Phase A's unlock endpoint creates. Phase A is now
  reachable by an operator (Wave 1); Phase B is still not (see task 4, unchanged).
- **DRY fingerprint composite**: met. One place (`HardwareFingerprintService`), consumed by both
  phases via the same method.
- **DRY credential refresh**: met. One handler, one refresher, three consumers, duplicated
  per-service code deleted.
- **Revocation never destroys data before the outbox drains**: **not applicable yet** — the
  enforcer that would destroy anything does not exist (task 7 not started).
- **`ActivationKeyStore.Load()` load-bearing**: **met**, see task 6 (Wave 1).
- **Retire `Build-SchoolRelease.ps1`**: **met**, see task 3 (Wave 1).

## What was dispatched but never landed (as of the wind-down before this resumption)

Two frontend/UI-adjacent slices were designed and written up as full dispatch briefs but never
successfully executed in the PRIOR session — every attempt to run them timed out at the
mandatory 420s wall-clock budget while still exploring the codebase, before writing a single
file:
- **Unit A (Phase A)**: `ActivationScreen.tsx` rewrite (passphrase + CUE picker), `MainForm.cs`
  rewiring away from the old bridge-message flow, `ActivationKeyStore.Load()` wiring (task 6),
  and retiring `Build-SchoolRelease.ps1` (task 3). **This unit landed in Wave 1 of this
  resumption** (see the "Resumption after runtime restart" section at the top of this file) —
  narrowed into 4 disjoint slices instead of one wide one, which is what got it across the
  line this time.
- **Unit B (Phase B)**: `EnrolmentScreen.tsx` (activation-key entry + client-side checksum
  validation), wiring into `HostApp.tsx`. **Still not attempted** — this is Wave 2's target.

## What was NOT verified, and what the substitutions do not prove

Per the coordinator's standing rule (the standard set by B8's PR #20): naming every substitution
plainly rather than letting a green local run imply more than it does.

- **No Windows machine exists in this environment.** Everything specific to Windows —
  `WindowsHardwareSignalReader`'s registry/volume-serial reads, `ActivationKeyStore`'s DPAPI
  encryption, the WinForms/WebView2 host itself — compiles (confirmed, `dotnet build
  PlanCope.slnx -warnaserror` is green including `PlanCope.Local.Host`, net8.0-windows, which DOES
  build here even though it cannot run here) but has never executed. Fingerprint stability across
  reboot/update/RAM-change, DPAPI round-tripping, and the WebView2 UI itself are all unverified by
  construction, not by omission.
- **No live Central instance is reachable here.** Every test that exercises "talk to Central" —
  `NodeCredentialRefresherTests`, `CentralCredentialHandlerTests` — fakes the HTTP layer with an
  in-process `HttpMessageHandler`. This proves the Local-side logic is correct against a Central
  that behaves exactly as `ActivationContracts.cs` describes; it proves nothing about Central's
  actual behaviour, error bodies, or latency. `EnrolmentEndpointsTests.cs` has an explicit skip
  rather than a fabricated pass, for the same reason plus the added one that no
  `WebApplicationFactory`-based fixture exists yet for a Local.Api endpoint that calls out.
- **No real encrypted roster bundle exists.** `EnvelopeDecryption.ListCuesAsync` and the
  `/unlock`/`/bundle-cues` endpoints are tested only against small synthetic bundles built in-test
  via `EnvelopeEncryption.EncryptDirectoryAsync` with 1-2 entries. Behaviour against a real
  ~1,440-CUE production bundle (file size, entry-scan performance, real GE roster data shapes) is
  unverified.
- **A green build/test run here is not a green CI run.** Per the batch's standing rule, nothing
  above is reported as a pass until CI (this branch's own run, once a PR exists) agrees. One known,
  pre-existing, out-of-scope CI failure has been flagged by the coordinator and is recorded here so
  it is not mistaken for something this batch broke:
  `PlanCope.Local.Api.Tests.ExamScoringPolicyPullTests.PullAsync_PersistsScoringPolicy_FromPublishedPackage`
  passes on Linux and fails on `windows-latest` CI with a Dapper `SyncState` materialization error.
  It is on `main` (from B3), predates this batch, is not touched by anything in this diff, and is
  tracked as an open defect by the coordinator — do not attempt to fix it inside a resumed B2.
- **CA1416 suppression, not a fix.** `LocalDataServiceCollectionExtensions.cs` registers
  `WindowsHardwareSignalReader` (marked `[SupportedOSPlatform("windows")]`) from a plain `net8.0`
  project, which trips CA1416 under `-warnaserror`. Suppressed locally with a scoped `#pragma
  warning disable/restore CA1416` and a one-line comment, not by weakening the analyzer project-
  wide — this is a real, understood platform boundary (the type is only ever resolved at runtime
  inside the Windows-only host), not a swept-under-the-rug warning.

## Migration numbering — two collisions, both caught before merge, worth reading if this recurs

The worktree branched from `65fd0b6` and could only see up to `008_Schools.sql`, so its first
migration was named `009_NodeIdentity.sql`. By the time it landed, `main` had merged B8's
`009_PerformanceIndexes.sql` — caught by the coordinator from outside the worktree, verified here
independently (`git fetch origin && git ls-tree -r origin/main --name-only`) before acting: renamed
to `010_NodeIdentity.sql` via `git mv` (never edited an already-applied number in place), rebuilt,
retested green, force-pushed with `--force-with-lease` (branch was minutes old, nothing else based
on it). Second collision: B3's PR #24 held `010_ExamVersionScoringPolicy.sql`/`011_Grading.sql` as
an OPEN PR — also caught by the coordinator, also verified independently here
(`gh pr view 24 --json files,state`) before acting: renamed again to `012_NodeIdentity.sql`, which
is what actually shipped. **The lesson for a future batch**: a hand-maintained global migration
sequence is only safe to number against a freshly fetched `main` AND a freshly checked `gh pr list`
for open PRs touching `Data/Migrations/` — this worktree's own view of `main` was correct but
incomplete both times, and only the coordinator's cross-branch visibility caught it.

## Commits in this batch (chronological)

1. `feat(local): add hardware fingerprint composite service` — task 1.
2. `feat(local): add node_identity schema and repository` — schema prerequisite for tasks 2/4/5/7.
3. `fix(local): renumber node_identity migration to 010` — first collision fix.
4. `docs(b2): log wave 1 and the migration-number rebase fix`
5. `fix(local): renumber node_identity migration to 012` — second collision fix.
6. `docs(b2): log the second migration-number rename`
7. `feat(local): add Phase A backend - bundle CUE listing and on-demand seeding` — task 2/3 backend, endpoints.
8. `feat(local): add Phase B enrolment and shared credential refresh` — task 4/5 backend, endpoints.
9. `test(local): cover NodeCredentialRefresher and CentralCredentialHandler` — closing the untested gap from commit 8.
10. `feat(local): wire fingerprint, node identity and enrolment into DI` — leader-written glue: DI registration and endpoint routing only, no new logic.
11. `docs(b2): record the batch mandate`
12. This document.

## What a future batch resuming B2 should do first

In order of what unblocks the most: (1) `ActivationScreen.tsx` + `MainForm.cs` rewiring for Phase
A — the backend is ready and tested, this is now a narrower task than the original brief since
`/api/activation/status`, `/bundle-cues`, `/unlock` already exist; (2) `ActivationKeyStore.Load()`
wiring (task 6), naturally paired with the same MainForm.cs work; (3) `EnrolmentScreen.tsx` +
`HostApp.tsx` wiring for Phase B, same reasoning; (4) `scripts/Build-SchoolRelease.ps1` retirement
(task 3, small, mostly deletion); (5) `RevocationEnforcer` (task 7) and the re-activation screen
(task 8), which have no code started at all and should be scoped as their own wave.
