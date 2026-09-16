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
