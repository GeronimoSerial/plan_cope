# B2 — Node enrolment and hardware identity: progress log

## Status: PARTIAL — backend only, no operator-facing UI. Do not read any task below as "done" without reading its status line.

Of the plan's 8 tasks under "### B2 ·" in `PROJECT-CLOSURE-PLAN.md`, **the backend halves of
tasks 1, 2, 4 and 5 are built and tested. Nothing an operator can actually click or type exists
yet** — no Phase A screen, no Phase B enrolment screen, no `ActivationKeyStore` wiring, no
revocation enforcement, no re-activation screen. Wind-down was called (owner instruction) before
any frontend slice landed. A reviewer should read this document task-by-task before looking at
the diff — several tasks look further along in code than they are in the actual product, because
the backend was built first and the UI that makes it reachable never was.

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
   — **PARTIAL, backend only**. `src/Local/PlanCope.Local.Api/Endpoints/ActivationEndpoints.cs`
   (`GET /api/activation/status`, `GET /api/activation/bundle-cues`, `POST /api/activation/unlock`)
   calls the REAL crypto path — `tools/PlanCope.RosterCrypto/EnvelopeDecryption.DecryptCueAsync`,
   unchanged, still doing genuine Argon2id key derivation and a real AES-GCM DEK unwrap, exactly
   as the plan's acceptance criterion names it. `EmbeddedRosterSeeder.SeedOneAsync` (new) imports
   the decrypted roster on success; `node_identity` and `schools` rows get created/updated
   correctly. **What does NOT exist**: the React `ActivationScreen.tsx` was never rewritten — it
   still posts a raw passphrase through the old WebView2 bridge message to
   `MainForm.Activate()`/`ActivationKeyStore.Store()`, a flow that has nothing to do with the new
   endpoints above. **An operator cannot complete Phase A through the actual application today.**
   The acceptance criterion "Phase A completes with the network cable unplugged, and a full exam
   session runs to submission afterwards" is **NOT met** — the crypto and persistence are real and
   tested, but there is no UI path to reach them.

3. **Remove the single-CUE build restriction / retire `Build-SchoolRelease.ps1`** — **PARTIAL**.
   `RosterBundleOptions.Cue` (the build-time constant) is removed;
   `EmbeddedRosterSeeder`/`EmbeddedRosterSource` now take the CUE as a runtime parameter
   (`SeedOneAsync(cue, passphrase, ...)`), which is the substantive part of "remove the
   single-CUE restriction." **`scripts/Build-SchoolRelease.ps1` itself was never touched** — it
   still exists, still references a `-p:RosterBundlePath` MSBuild property that
   `PlanCope.Local.Host.csproj` has never consumed (verified: grepped the `.csproj`, no match —
   this script predates the encrypted-bundle mechanism and was already dead before this batch
   started), and still downloads one CUE's plaintext `roster.json` directly rather than going
   through `tools/PlanCope.RosterCrypto`'s `pack` command. **Not started, not retired, still on
   disk exactly as it was.**

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

6. **`ActivationKeyStore.Load()` made load-bearing** — **NOT STARTED**. Still dead code, exactly
   as it was before this batch (`src/Local/PlanCope.Local.Host/Services/ActivationKeyStore.cs:38-49`).
   This was scoped to the frontend/Host slice that never ran (see "What was dispatched but never
   landed" below).

7. **`RevocationEnforcer`** (wait for session end → drain outbox → wipe → lock, resumable via
   `node_identity.revocation_stage`) — **NOT STARTED**. No design work beyond the schema column
   (`node_identity.revocation_stage`, added in the merged `012_NodeIdentity.sql` migration) exists.
   Nothing in this batch executes any part of the fixed sequence in plan §2.8.

8. **Re-activation screen for a locked node** — **NOT STARTED**.

## Cross-cutting gates from the batch mandate (`_briefs/B2-LEADER.md`) — checked honestly

- **Offline-first (Phase A fully offline)**: the crypto and persistence this criterion cares about
  are real and tested (task 2's `DecryptCueAsync` call is unchanged, genuine Argon2id + AES-GCM).
  The criterion as a whole is **NOT met**, because there is no UI path for an operator to reach
  it. Do not report this gate as closed based on the endpoint tests alone.
- **Two-phase separation**: respected structurally — Phase A (`ActivationEndpoints`) and Phase B
  (`EnrolmentEndpoints`) are separate endpoint groups, separate concerns, and Phase B's redeem call
  requires a `node_identity` row that only Phase A's unlock endpoint creates. Neither phase is
  reachable by an operator yet, so "separation" is unverified as a *product* property, only as a
  *code* property.
- **DRY fingerprint composite**: met. One place (`HardwareFingerprintService`), consumed by both
  phases via the same method.
- **DRY credential refresh**: met. One handler, one refresher, three consumers, duplicated
  per-service code deleted.
- **Revocation never destroys data before the outbox drains**: **not applicable yet** — the
  enforcer that would destroy anything does not exist (task 7 not started).
- **`ActivationKeyStore.Load()` load-bearing**: **not met**, see task 6.
- **Retire `Build-SchoolRelease.ps1`**: **not met**, see task 3.

## What was dispatched but never landed

Two frontend/UI-adjacent slices were designed and written up as full dispatch briefs but never
successfully executed — every attempt to run them timed out at the mandatory 420s wall-clock
budget while still exploring the codebase, before writing a single file:
- **Unit A (Phase A)**: `ActivationScreen.tsx` rewrite (passphrase + CUE picker), `MainForm.cs`
  rewiring away from the old bridge-message flow, `ActivationKeyStore.Load()` wiring (task 6),
  and retiring `Build-SchoolRelease.ps1` (task 3).
- **Unit B (Phase B)**: `EnrolmentScreen.tsx` (activation-key entry + client-side checksum
  validation), wiring into `HostApp.tsx`.

Both were re-decomposed into narrower backend-only slices (dropping all React/`MainForm.cs`
scope) which DID land successfully — that is everything marked DONE/PARTIAL above. The frontend
scope itself was never re-attempted after wind-down was called; it is simply not built. A future
batch resuming B2 should treat these two slices as the very next work, not as already attempted
and failed — the backend they depend on (`/api/activation/*`, `/api/enrolment/*`) is real, tested,
and wired into the running app (`LocalApiApplication.cs`'s `MapActivationEndpoints()`/
`MapEnrolmentEndpoints()`), so the frontend work is now unblocked and should be considerably
narrower than the original brief.

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
