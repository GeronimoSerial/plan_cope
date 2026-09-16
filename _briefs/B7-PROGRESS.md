# B7 · Gated updates, integrity and rollback — progress

Leader: B7 Sonnet batch lead. Implementers: `opencode-go/deepseek-v4-flash` via
`scripts/LEVEL3-DISPATCH-PROTOCOL.md`. **CODE-COMPLETE, NOT FULLY VERIFIED — read the
PROVEN/NOT PROVEN table before treating this as done.**

## Plan-vs-reality correction

`PROJECT-CLOSURE-PLAN.md` §5 B7 task 3 describes `GET /api/updates/feed` as a generic
authenticated JSON endpoint. **That does not hold against the real Velopack client.** Verified
directly against `Velopack` 0.0.1251 (the version this repo pins), by constructing
`Velopack.Sources.SimpleWebSource` with a capturing `IFileDownloader` and calling
`GetReleaseFeed` — pure C#, no Windows dependency, so it ran in this environment, not a guess:

- **Request**: `SimpleWebSource` requests
  `GET {baseUri}/releases.{channel}.json?arch={arch}&os={os}&rid={rid}&id={packageId}&localVersion={currentVersion}`,
  not an arbitrary path. The route must be literally `releases.{channel}.json`.
- **Response**: the body must deserialize via `Velopack.VelopackAssetFeed.FromJson`. Verified
  round-trip: `{"Assets":[{"PackageId":"...","Version":"1.4.0","Type":1,"FileName":"...","SHA1":"...","SHA256":"...","Size":12345,"NotesMarkdown":"...","NotesHTML":null}]}`
  — `Version` is a plain semver **string**, `Type` is the enum's underlying **int** (`Full = 1`).
- **Auth**: `UpdateOptions` and `SimpleWebSource` have no bearer-token hook. Auth requires a
  custom `IFileDownloader` (`BearerAuthFileDownloader`, subclasses `HttpClientFileDownloader`,
  overrides `CreateHttpClient`) attaching `Authorization: Bearer <token>` on every request.
- **Node identity is not in the request**: Velopack sends `id`/`localVersion`, never a node id.
  The feed controller resolves the caller from the JWT's `node_id` claim
  (`TokenService.CreateNodeAccessToken`), not a query parameter the way
  `SyncController.Pull` trusts `nodeId` — that existing pattern does not survive contact with a
  client that can't be told to send one. `SyncController` itself is untouched.

**What does not change**: D6. The feed serves only from an authenticated Central route
(`[Authorize]` + `token_type == node_access`, via the extracted `NodeAccessAuth` helper), never
a public GitHub Release asset.

`stagingId` (Velopack's own client-side percentage-rollout hook) is **not** sent in the request
at all — confirmed by the same probe. `ReleaseGateService`'s own deterministic-percentage gate
is the only place rollout percentage is decided; Velopack's staging mechanism is unused.

## What shipped (12 commits, `feat/b7-gated-updates`)

| Task | What | Commit(s) |
|---|---|---|
| 1 — release rings + gating | `sync.release_rings` schema; `ReleaseGateService` resolves eligibility (unregistered/up-to-date node → nothing; `AllEnrolled`; `PercentageOfEnrolled` via deterministic SHA-256(nodeId:ringId) mod 100 bucket, no `Random`; `ExplicitList` deferred, ineligible) | `e7a4cca`, `1eb235d` |
| 2 — SHA-256 recorded at release time | `release_rings.Sha256` never empty by construction (NOT NULL column); the feed always serves it from that column, never from `GitHubReleaseInstallerStorage`'s empty-string gap | `e7a4cca` |
| 3 — per-node feed | `GET /api/updates/releases.{channel}.json`, real Velopack wire protocol, node-access-token gated (D6) | `599120c` |
| 4 — wire `UpdateService` in | Backend: `BearerAuthFileDownloader` + `SimpleWebSource` (`ec8443f`). Host: constructed in `Program.cs`/`MainForm.cs`, IPC (`host:checkForUpdates`/`host:confirmRestart`/`host:updateStatus`) (`5b41a03`, `6403fca`). Frontend: `UpdateStatus.tsx` wired via new `useUpdateStatus` hook, rendered from `AppShell`'s footer, Spanish copy (`7999dde`) | `ec8443f`, `5b41a03`, `7999dde`, `6403fca` |
| 5 — verify SHA-256 before apply | `VelopackUpdateBackend.Sha256Matches`, fails closed, never throws on mismatch; `TryApplyAndRestart` already refused whenever not ready | `adcb033` |
| 6 — never mid-session, confirm before restart | `EvaluateSessionGateAsync` polls `/api/sessions/active` after download; **re-checks again inside `HandleConfirmRestartAsync` immediately before calling `TryApplyAndRestart`** — a session can start between the UI showing "ready" and the operator clicking confirm, so the gate is re-verified at the point of no return, not just when the button appeared. A failed session check fails closed (assumes active) | `5b41a03` |
| 7 — automatic rollback | `UpdateHealthTracker`: one grace startup per pending version, rollback target chained forward through every successful update; `VelopackUpdateBackend.TryRollBack` re-applies the last known-good local package with `AllowVersionDowngrade`; `Program.cs` evaluates this before `Application.Run`, every failure mode falls through to a normal launch | `1e17ba1`, `6403fca` |
| 8 — health reporting | `POST /api/updates/health` (node-access gated, `NodeAccessAuth` extracted for reuse); `MainForm.ReportHealthAsync` fires on every successful WebView load, fire-and-forget | `ec6ac55`, `6403fca` |
| 9 — D11 survival (partial) | `DataDirectorySurvivalTests.cs`: resolver layout stability, checksum-manifest determinism, and a compiled+source-level proof that update-path code never references the data directory. **Does not and cannot prove** the real OS-level cycle — see below | `2957011` |

## PROVEN / NOT PROVEN

| Item | Status | Reason |
|---|---|---|
| Migration/schema correctness | PROVEN | EF migrations generated (not hand-written), entity configs reviewed, `dotnet build` green |
| `sync.release_rings` migration applies to a real database | NOT PROVEN | No PostgreSQL instance reachable in this environment |
| Release-gate eligibility logic (all rollout modes, determinism) | PROVEN | 5 unit tests, deterministic-bucket logic verified across repeated calls |
| Feed wire-protocol correctness (route, JSON shape, auth claim) | PROVEN | Verified by constructing Velopack's own `SimpleWebSource`/`VelopackAssetFeed.FromJson` against the pinned package version and round-tripping the exact response shape |
| D6 (feed never public, node-access-token gated) | PROVEN | Tests assert 403 on missing/wrong claim; route only exists behind `[Authorize]` |
| SHA-256 verification logic (match/mismatch/missing-file) | PROVEN | Unit tests on `Sha256Matches` in isolation |
| SHA-256 verification against a real Velopack download | NOT PROVEN | `VelopackLocator.IsCurrentSet` is only true inside an installed app; unreachable outside Windows |
| Session-gate re-check race (confirm → session starts → refused) | PROVEN AS CODE PATH | `TryApplyAndRestart` is reachable only through `HandleConfirmRestartAsync`, which re-queries `/api/sessions/active` immediately before calling it — read the code path, not exercised end-to-end (needs a running Local.Api + WinForms host) |
| Rollback state machine (one grace startup, chained target, corrupt-marker tolerance) | PROVEN | 7 unit tests, pure file I/O, zero Velopack/WinForms dependency |
| Rollback actually re-applying a local package via Velopack | NOT PROVEN | `TryRollBack`'s `ApplyUpdatesAndRestart` call is unreachable without a real Velopack-installed app |
| Health report delivery to Central | PROVEN (Central side) | `POST /api/updates/health` tested: auth gate, persistence, both healthy/unhealthy paths |
| Health report actually sent by a running Local Host | NOT PROVEN | `MainForm.ReportHealthAsync` fires from `OnNavigationCompleted`, unreachable without a real WebView2 host |
| `dotnet build PlanCope.slnx -warnaserror` | PROVEN | Green throughout, including `PlanCope.Local.Host` (`net8.0-windows` compiles on Linux, does not run) |
| Full solution test suite | PROVEN | 27/27 `Local.Host.Tests`, 85/85 `Central.Api.Tests`, 34/34 ClientApp vitest, all green as of the last commit |
| Velopack packaging (`vpk pack`) | NOT PROVEN, BY CONSTRUCTION | Requires a real Windows build agent |
| Real update-and-restart cycle end to end | NOT PROVEN, BY CONSTRUCTION | Requires a real Windows install produced by `vpk pack` |
| `%LocalAppData%\PlanCope\` survives a real update (D11, full scenario) | NOT PROVEN, BY CONSTRUCTION | `docs/velopack-test-matrix.md` Scenario 3 itself says this "must be run by hand" on real hardware; `DataDirectorySurvivalTests.cs` proves the narrower, code-level guarantee only |
| SmartScreen/unsigned-binary warning path | NOT PROVEN, BY CONSTRUCTION | Owner-accepted risk per the plan, Windows-only, not B7's own work |

**What would close every "NOT PROVEN, BY CONSTRUCTION" row**: a real win-x64 machine, a
Velopack-produced installer from `vpk pack` off this branch, and a walk through
`docs/velopack-test-matrix.md` scenarios 1–3 plus a forced-start-failure rollback test and a
corrupted-payload test (neither of which the doc currently covers — they'd need authoring
alongside that hardware run, since they're net-new test cases this batch didn't have a template
for).

## Known gap, not closed in this PR

**Nothing writes to `release_rings`.** `ReleaseGateService` and `UpdatesController.Releases`
both read it; there is no endpoint to create a row. A release can be built and its SHA-256
computed, but nothing registers it with Central yet. A dispatch for an admin-authenticated
`POST /api/admin/updates/rings` endpoint (role="Admin", validates a non-empty SHA-256 before
accepting a registration — the same D6-adjacent guarantee the feed depends on) was in flight
when this PR was cut and was stopped, not merged, per the instruction not to accumulate further
scope. This is real, necessary follow-up work — B7's gating and feed are otherwise inert without
it — but it is administrative/CI-integration work, not part of the update-client surface B9's
E2E scenario needs to exercise.

## For B9's end-to-end scenario

- The update-client surface (check → download → verify → session-gate → confirm → apply,
  rollback, health report) is fully wired and unit-tested, but **nothing in this repo currently
  calls the admin registration endpoint that doesn't exist yet** (see gap above) — B9 cannot
  exercise a real "node receives and applies an update" path without that endpoint existing and
  a `release_rings` row being inserted (by hand, by a test fixture, or once the admin endpoint
  above lands).
- `PLANCOPE_UPDATE_FEED_URL` and `PLANCOPE_UPDATE_CHANNEL` are the two environment variables that
  gate whether `MainForm` constructs an `UpdateService` at all. Unset, the host behaves exactly
  as it did before this batch (update UI shows `notConfigured`, nothing else changes) — this is
  deliberate graceful degradation, not a bug, but B9 needs both set to exercise any of this.
- The health-marker file lives at `{DataDirectoryResolver.ConfigDirectory}/update-health.json` —
  outside `data`/`assets` (D11's protected paths), so seeding or inspecting it for a rollback
  test does not touch anything D11 protects.
- Rollback can only ever target a version this tracker itself previously applied — the
  originally-installed package (from `vpk pack`, not from an update) has no recorded
  `FileName`/`Sha256`/`DownloadUrl` and cannot be a rollback target. A rollback test needs at
  least two applied updates in sequence (so the marker has a real chain to fall back through),
  not just one.

## Post-merge addendum, for B9 (2026-09-16)

B7 merged as `dc00cb5`. Everything below is what I know about the update leg's real behavior
that is **not** already expressed in code or a test — the category B9 cannot reconstruct from
reading the diff and would otherwise have to rediscover by hand.

### Velopack behavior discovered but not directly testable here

- **`VelopackLocator.IsCurrentSet` gates the entire update surface's real behavior.** It is only
  true inside an app launched from a genuine Velopack install (produced by `vpk pack` and run
  through its installer). Every Velopack-touching method in this batch
  (`VelopackUpdateBackend.DownloadUpdatesAsync`, `TryRollBack`, `MainForm.GetInstalledAppVersion`)
  checks this first and no-ops safely (`false`/`null`) when it's not set. Practically: if B9 runs
  the E2E scenario by launching `PlanCope.Local.Host.exe` directly from a build output directory
  — not through a `vpk`-installed shortcut — the entire update leg will silently behave as "not
  installed," not error. A test that "passes" that way has not exercised anything; it needs a
  real `vpk pack` → install → launch cycle to mean anything.
- **`VelopackApp.Build().Run()` must stay the first statement in `Main()`.** It intercepts
  OS-level activation arguments Velopack's installer passes on first-run/after-update/uninstall.
  If B9's harness ever wraps or replaces `Program.Main` (e.g. to inject test hooks), this call
  must still run first and unmodified, or the app won't correctly recognize a post-update
  restart.
- **The retained local package directory (`VelopackLocator.Current.PackagesDir`) is NOT under
  `%LocalAppData%\PlanCope\`.** It lives under Velopack's own install root. This matters for two
  things B9 should know before designing test steps: (1) it is untouched by D11's protected
  paths, so cleaning it between test runs is safe for data-survival purposes but (2) `TryRollBack`
  depends on the previous version's `.nupkg` still being there — if a test harness wipes the
  install directory for "clean state" between steps, rollback will always report "no local
  target" (fails closed to a normal launch, per design, but the rollback scenario itself won't be
  exercised). A rollback E2E test needs the install directory left alone across the two
  sequential updates it requires.
- **No code in this repo ever calls `UpdateManager.CleanPackagesExcept`.** Old package files
  accumulate on disk across every update, indefinitely. Not a defect introduced by B7 and not
  something this batch's scope covered — flagging it because a long-running E2E environment that
  applies many updates in sequence will see disk usage grow, and that is expected, not a bug to
  chase.
- **Channel precedence**: `vpk pack --channel <x>` sets Velopack's *default* channel baked into
  the installer; `UpdateOptions.ExplicitChannel` (which `VelopackUpdateBackend` always sets
  explicitly, from `PLANCOPE_UPDATE_CHANNEL`) overrides it unconditionally. If B9 packages a
  build with one channel and sets the env var to a different one, the env var wins — keep them
  consistent in test setup to avoid confusing results, not because the code would misbehave.

### What `DataDirectorySurvivalTests.cs` actually covers, precisely

It proves three things, all at the PlanCope-code level: the resolver's directory layout is
stable under an explicit root; a checksum-manifest harness is internally deterministic (a
stability check on the harness itself); and no update-path source (`UpdateService.cs`,
`VelopackUpdateBackend`) references `DataDirectoryResolver`, `PLANCOPE_DATA_DIR`, or
`LocalApplicationData`, checked both by reflection over compiled types and by a source-text
scan. **It says nothing about Velopack's own internals.** If Velopack itself ever wrote outside
its documented install/package paths — a bug in the library, not in PlanCope — this test
would not catch it, because it only checks PlanCope's own code never reaches toward the data
directory. Only `docs/velopack-test-matrix.md` Scenario 3, run by hand with a real before/after
checksum over `%LocalAppData%\PlanCope\`, closes that gap.

### Argued by construction, not exercised — what specifically was never run

- **Feed protocol correctness** was verified by constructing Velopack's own `SimpleWebSource`
  and `VelopackAssetFeed.FromJson` in an isolated probe and round-tripping the exact response
  bytes `UpdatesController` produces. That is strong evidence — real Velopack code parsed the
  real bytes — but it was never exercised over an actual HTTP connection between a running
  `UpdateManager` and a running Central instance. One real check worth B9 doing early: point a
  genuinely installed app at a real Central deployment's feed URL and confirm
  `CheckForUpdatesAsync()` returns a non-null `UpdateInfo` when a matching `release_rings` row
  exists.
- **SHA-256 verification's file-path construction**
  (`VelopackLocator.Current.PackagesDir` + `TargetFullRelease.FileName`) was copied from the
  pattern already present in this file before B7 touched it, not independently confirmed against
  a real Velopack download. If that path assumption is wrong, `DownloadUpdatesAsync` would
  report every download as a checksum failure even when the download actually succeeded — worth
  being the very first thing checked if a real update test shows downloads always "failing
  integrity."
- **Rollback's `VelopackAsset` construction is the highest-risk unverified piece.** `TryRollBack`
  builds a `VelopackAsset` with `PackageId` from `VelopackLocator.Current.AppId` and `Size` from
  the local file's actual byte length — reasonable approximations, but never checked against a
  `VelopackAsset` Velopack itself produced for the same package, since none was reachable in this
  environment. If `ApplyUpdatesAndRestart` validates any field more strictly than assumed here,
  it could throw at the exact moment a real rollback is needed. This is the one piece of task 7
  I'd want run by hand before trusting it in production, not just before merging.
- **The session-gate re-check** (`HandleConfirmRestartAsync` re-querying `/api/sessions/active`
  immediately before `TryApplyAndRestart`) was verified by reading the code path and confirming
  `TryApplyAndRestart` has no other caller — never exercised through an actual WinForms UI click
  with a session genuinely starting in the race window. The logic is sound on inspection; the
  timing was never watched happen.
