# Velopack manual test matrix (B6.T10)

Manual verification procedure for the Velopack packaging and update pipeline built in
Batch 6. These scenarios are **not** automatable in CI (they require a real Windows
machine, a real installer produced by `vpk`, and observing OS-level install/update
behavior) and must be run by hand before each release that changes packaging, update,
or persistence-path logic.

Persistent data lives under the directory resolved by `DataDirectoryResolver`
(`%LOCALAPPDATA%\PlanCope` by default, or `PLANCOPE_DATA_DIR` if set), specifically its
`data`, `assets`, and `config` subdirectories.

Do not use the real padrón (student roster) ZIP or real student data in any of these
scenarios — use synthetic/fixture data only, exactly as in automated tests.

## Scenario 1 — Clean install

**Goal**: a machine with no prior PlanCope installation ends up with a working app.

Steps:
1. On a clean Windows VM/user profile (no `%LOCALAPPDATA%\PlanCope` directory, no prior
   PlanCope install), run the Velopack-produced installer (`Setup.exe` / installer
   package from `vpk pack`).
2. Launch the installed app from the Start Menu shortcut Velopack creates.
3. Confirm the app starts, the WebView2-hosted UI renders, and the installed version
   shown in the UI ("Buscar actualizaciones" panel) matches the packaged version.
4. Confirm `%LOCALAPPDATA%\PlanCope` (or the configured `PLANCOPE_DATA_DIR`) was
   created with `data`, `assets`, `config`, and `logs` subdirectories.
5. Load a synthetic/fixture dataset (never the real padrón) and confirm basic
   read/write flows work (e.g. create a session, view a roster entry).

Expected result: install completes without SmartScreen blocking the run entirely (only
the "Windows protected your PC" warning is expected with the self-signed PFX per
decision 7 — "More info → Run anyway" must be documented for testers); app launches;
persistent directories exist; synthetic data flows work end-to-end.

Result: ☐ Pass ☐ Fail — notes: ______________________________________________

## Scenario 2 — Update N-1 → N

**Goal**: an existing install picks up a newer release without manual reinstall and
without losing data.

Steps:
1. Start from a machine with version `N-1` installed (from Scenario 1, or a prior
   release channel build) with synthetic data already present in the data directory.
2. Publish/host a version `N` release (delta + full) reachable from the configured
   update feed URL and channel (`stable` or `beta`).
3. Launch the `N-1` app, let the main window become operative, then trigger the update
   check via the visible "Buscar actualizaciones" action (or let the non-blocking
   background check find it).
4. Confirm the app shows that an update was found, downloads it in the background
   without freezing the UI, and does **not** restart automatically.
5. Confirm the explicit restart/apply confirmation prompt appears, and only proceeds
   to install/restart after the user confirms.
6. After restart, confirm the app now reports version `N` in the UI.

Expected result: update download does not block the UI thread; no restart happens
without explicit user confirmation; after confirming, the app relaunches on version
`N`.

Result: ☐ Pass ☐ Fail — notes: ______________________________________________

## Scenario 3 — Persistent data survives the update

**Goal**: the update in Scenario 2 does not touch or lose data under the persistent
data directory.

Steps:
1. Before updating (still on version `N-1`), record a checksum or listing of the
   synthetic data directory (`data`, `assets`, `config` subdirectories) — e.g.
   `Get-ChildItem -Recurse | Get-FileHash`.
2. Perform the update to version `N` exactly as in Scenario 2.
3. After the app relaunches on version `N`, re-run the same checksum/listing over the
   data directory.
4. Confirm the synthetic records created before the update are still present and
   unmodified (same checksums for files not expected to change, e.g. the SQLite DB
   file still contains the same rows), and that no leftover per-version data directory
   was created by Velopack outside the app's own resolved data path.

Expected result: the persistent data directory and its synthetic content are
byte-identical (aside from expected application-owned writes, e.g. new log lines)
before and after the update — no data loss, no path change.

Result: ☐ Pass ☐ Fail — notes: ______________________________________________

## Sign-off

| Scenario | Result | Tester | Date | Build/version tested |
|----------|--------|--------|------|-----------------------|
| 1. Clean install | | | | |
| 2. Update N-1 → N | | | | |
| 3. Data survives update | | | | |
