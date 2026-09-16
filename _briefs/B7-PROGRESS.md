# B7 · Gated updates, integrity and rollback — progress

Leader: B7 Sonnet batch lead. Implementers: `opencode-go/deepseek-v4-flash` via
`scripts/LEVEL3-DISPATCH-PROTOCOL.md`. This file is updated after every wave, not only at
the end — see `docs/DELEGATION-RULES.md` rule 12.

## Plan-vs-reality correction (recorded per coordinator instruction, 2026-09-16)

`PROJECT-CLOSURE-PLAN.md` §5 B7 task 3 describes `GET /api/updates/feed` as a generic
authenticated JSON endpoint. **That does not hold against the real Velopack client.**

Verified directly against `Velopack` 0.0.1251 (the version this repo pins), by constructing
`Velopack.Sources.SimpleWebSource` with a capturing `IFileDownloader` and calling
`GetReleaseFeed` — this is pure C#, no Windows dependency, so it runs on this Linux
environment and is not a guess:

- **Request shape**: `SimpleWebSource` requests
  `GET {baseUri}/releases.{channel}.json?arch={arch}&os={os}&rid={rid}&id={packageId}&localVersion={currentVersion}`
  via `IFileDownloader.DownloadString`, not an arbitrary path. The route must be literally
  `releases.{channel}.json`, not `/feed`.
- **Response shape**: the body must deserialize via `Velopack.VelopackAssetFeed.FromJson`.
  Verified round-trip: `{"Assets":[{"PackageId":"...","Version":"1.4.0","Type":1,"FileName":"...","SHA1":"...","SHA256":"...","Size":12345,"NotesMarkdown":"...","NotesHTML":null}]}`
  — `Version` is a plain semver **string** (not the nested object plain `JsonSerializer`
  produces without a custom converter — `Velopack.Util.SemanticVersionConverter` exists but is
  `internal`, so the feed writer needs its own `JsonConverter<SemanticVersion>` that reads/writes
  a plain string), `Type` is the enum's underlying **int** (`Full = 1`), `SHA256` is the field
  this whole gate exists to populate honestly (never empty, unlike
  `GitHubReleaseInstallerStorage.cs:125`).
- **Auth**: `UpdateOptions` has no bearer-token hook, and `SimpleWebSource` does not expose one
  either — the `authorization` parameter it passes to `IFileDownloader` is empty by default.
  Node-credential auth is only possible by supplying a **custom `IFileDownloader`** (subclass
  `HttpClientFileDownloader`, override `CreateHttpClient`/`CreateHttpClientHandler` to attach
  `Authorization: Bearer <node access token>` to every request) passed into
  `new SimpleWebSource(baseUri, customDownloader, timeout)`, then that `IUpdateSource` into
  `UpdateManager`'s `(IUpdateSource, UpdateOptions, IVelopackLocator)` constructor — not the
  `(string urlOrPath, ...)` one `VelopackUpdateBackend` currently uses.
- **Node identity is not in the request**: Velopack supplies `id` (package id) and
  `localVersion`, never a node id. The feed controller must resolve the calling node from the
  validated JWT's `node_id` claim (`TokenService.CreateNodeAccessToken`,
  `src/Central/PlanCope.Central.Api/Auth/TokenService.cs:21-22`), the same claim `cue` is
  carried in — **not** an explicit query parameter the way `SyncController.Pull` trusts
  `nodeId` (`SyncController.cs:31`). That existing pattern doesn't survive contact with a
  client that can't be told to send one. This is a deliberate, protocol-forced deviation, not a
  redesign of `SyncController`'s auth — `SyncController` is untouched.

**What does not change**: D6. The feed still serves only from an authenticated Central route,
never a public GitHub Release asset. The `[Authorize]` requirement must additionally check
`token_type == node_access` (`TokenService.cs:14`) so a user/operator bearer token cannot list
or fetch update packages — narrower than `SyncController`'s bare `[Authorize]`, and worth it
given D6's stated failure mode (227,598 minors' data via a public/leaked installer).

Confirmed via the same probe: `stagingId` (Velopack's own percentage-rollout hook) is **not**
sent in the request at all — so it cannot substitute for `ReleaseGateService`'s own
deterministic-percentage gating. B7 task 1's gate service remains the only place rollout
percentage is decided; Velopack's client-side staging mechanism is unused.

## Waves completed

### Wave 1 (parallel, disjoint) — commits `e7a4cca`, `adcb033`, `2957011`

1. **`sync.release_rings` schema (Central)** — `ReleaseRing` record, EF configuration, generated
   migration `20260916115432_AddReleaseRings`. Columns: Version, Channel, Sha256, DownloadUrl,
   RolloutMode, RolloutPercentage, CreatedAt, CreatedBy. Reviewed diff, built, migration
   generated (not hand-written). **Not verified**: applying against a real PostgreSQL instance —
   no database reachable in this environment; the generated `Up`/`Down` were read and match the
   entity configuration exactly.
2. **SHA-256 verification before apply (Local)** — `UpdateService`/`VelopackUpdateBackend`
   (task 5). `IUpdateBackend.DownloadUpdatesAsync` now takes `expectedSha256`, fails closed
   (never throws) on mismatch, `TryApplyAndRestart` already refused whenever `_downloadReady`
   is false so no second guard was needed. 5 new tests, all passing. **Not verified**: whether
   `VelopackLocator.Current.PackagesDir` + `_pendingUpdate.TargetFullRelease.FileName` actually
   resolves to the downloaded file's real on-disk path during a genuine Velopack download cycle
   — only reachable on a real Windows install (`VelopackLocator.IsCurrentSet` is false outside
   an installed app, which this environment cannot produce).
3. **D11 data-survival test (Local)** — `DataDirectorySurvivalTests.cs` (task 9, partial).
   Proves `DataDirectoryResolver`'s layout is stable and that `UpdateService`/
   `VelopackUpdateBackend` source never references the resolver, `PLANCOPE_DATA_DIR`, or
   `LocalApplicationData` (compiled + source-level assertion). **Explicitly does not and cannot
   prove** the real OS-level install/update cycle leaves `%LocalAppData%\PlanCope\`
   byte-identical — that stays `docs/velopack-test-matrix.md` Scenario 3, run by hand on real
   Windows hardware. Closing that gap needs a Windows machine, a real `vpk`-packaged installer,
   and a tester following the doc.

### Wave 2 (partial)

4. **Release-gate resolution service (Central)** — commit `1eb235d`. `IReleaseGateService` /
   `ReleaseGateService` (task 1). Resolves eligibility from the newest `release_rings` row per
   channel: unregistered node → nothing; node already on the newest version → nothing;
   `AllEnrolled` → everyone; `PercentageOfEnrolled` → deterministic SHA-256(nodeId:ringId) mod
   100 bucket (same node always gets the same in/out answer for the same ring — no `Random`);
   `ExplicitList` → deferred, returns ineligible, one-line comment noting the membership table
   doesn't exist yet rather than guessing a schema. 5 tests passing.

## Not yet dispatched

- **Task 3 — per-node feed controller (Central)**: now fully specified per the protocol
  correction above (exact route, exact JSON shape verified by round-trip, exact auth claim).
  About to dispatch.
- **Task 4 — wire `UpdateService` into the running app (Local)**: needs a custom
  `IFileDownloader` for node-credential auth (see correction above), plus IPC wiring into
  `MainForm.cs`/`Program.cs` and `UpdateStatus.tsx`/`HostApp.tsx`. `HostContext` already declares
  unused `appVersion`/`updateChannel` fields (`types.ts:8-9`) — this task fills them, does not
  invent new ones.
- **Task 6 — never update mid-session**: `GET /api/sessions/active` already exists
  (`SessionEndpoints.cs:17`, backed by `ISessionRepository.GetActiveAsync`) — no new Local.Api
  surface needed, just a caller from `Local.Host` before offering/applying an update.
- **Task 7 — automatic rollback on failed start**: not started. Needs research into what
  "failed start" detection Velopack itself offers (`UpdateManager.UpdatePendingRestart` /
  crash-loop detection) versus what PlanCope must build itself. Unverified territory: cannot
  exercise a real crash-and-rollback cycle without Windows.
- **Task 8 — report post-update health to Central**: not started. Two-sided (Local sender +
  Central receiver), disjoint files, safe to dispatch as its own slice once task 4's host
  wiring exists to know when "just updated" is true.

## What cannot be verified in this environment, by construction

The win-x64 WinForms + WebView2 host does not run on Linux. Specifically unverifiable here,
named per the leader mandate:

- Velopack packaging (`vpk pack`) and the real install/update/apply/restart cycle.
- `VelopackLocator.Current` ever being populated (`IsCurrentSet` is only true inside an
  installed app) — so the SHA-256 verification path in `VelopackUpdateBackend` is unit-tested
  against its pure logic (`Sha256Matches`) but never exercised through a real download.
- `%LocalAppData%\PlanCope\` surviving a real update — `DataDirectorySurvivalTests.cs` proves
  the narrower, testable claim (see wave 1 item 3); the full scenario needs
  `docs/velopack-test-matrix.md` Scenario 3 run by hand.
- SmartScreen / unsigned-binary warning behavior (owner-accepted risk per the plan, unrelated
  to B7's own work but part of the install path B7's rollback logic sits behind).

Closing all of the above needs a real Windows machine with the actual installer built by
`vpk pack`, per the plan's own instruction.
