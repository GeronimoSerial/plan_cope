# Exam delivery to a Local node

A fresh install ships **no** Central exams. It gets real exams through sync, never by rebuilding
the installer.

## Fresh install

1. Activate/enrol the node (`/api/enrolment/redeem`) so `central_url`, `node_id` and the node
   credentials exist in `sync_state`.
2. The background sync (`SyncBackgroundService`) pulls published exam packages from Central on
   its idle tick (about every 30 seconds, paused while a delivery session is active) and upserts
   them locally.
3. An operator can also pull on demand with the **Buscar exámenes nuevos** button in the host UI,
   which calls `POST /api/sync/pull-exams`. The call pages until Central reports `hasMore=false`
   (capped at 2500 packages) and answers with a stable JSON shape:
   `{ status, newExams, updatedExams, totalReceived, errorCode, message, lastPullAt }`.

## An exam created after the build

Publishing the exam on Central creates a published package. Installed nodes receive it through
the normal sync (~30 s) or the on-demand button; there is no build step involved. If the exam is
still a draft, or its version has no published package, no node will ever see it.

## Demo exams

`LocalDemoExamSeeder` is development-only. `Local:SeedDemoExam` defaults to `false` in
production (`appsettings.json` and the code default in `LocalApiApplication`), and `true` only in
`appsettings.Development.json` / the dev launch profile. A release/Velopack build therefore never
seeds compiled-in demo exams for a real school. The seeder itself is kept.

Existing installed nodes that already seeded the demo exams keep that data: this change does not
delete anything. Remove those rows manually only if the operator wants a clean catalog.
