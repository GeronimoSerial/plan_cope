# Central exam publishing contract

Audience: the web-UI leader building the authoring screens, and any node-side integrator.
Scope: the Central API endpoints that take an exam from creation to a package that installed nodes
receive. All paths are relative to the Central API base URL. All endpoints require
`Authorization: Bearer <access token>` unless noted. JSON uses camelCase; `blockType` is serialized
as a string (e.g. `"TrueFalse"`).

Every write action in `ExamsController` requires role `Admin` or `ExamAuthor`; an authenticated
caller with another role receives `403`. The read actions retain the normal authenticated-user
requirement.

Source of truth: `src/Central/PlanCope.Central.Api/Controllers/ExamsController.cs`,
`src/Central/PlanCope.Central.Api/Controllers/SyncController.cs`,
`src/Shared/PlanCope.Shared.Contracts/Exams/ExamContracts.cs`.

---

## 1. Full sequence

```
POST /api/exams                                  create exam + initial empty draft version
        |
        v   examSummary.initialVersionId
PUT  /api/exams/versions/{versionId}/document    (builder) replace blocks and answer keys
  or PUT /api/exams/versions/{versionId}/blocks  (single block upsert)
        |
        v   version is READY when blockCount >= 1
POST /api/exams/versions/{versionId}/publish     emit PublicationPackage (Status=Published)
        |
        v   examSummary.publishedVersion* now points at this version
GET  /api/sync/pull?nodeId=&cursor=&limit=       node receives the package

Editing a published exam (published versions are immutable):
PUT  /api/exams/{examId}                         (metadata only; code is immutable)
POST /api/exams/{examId}/versions                deep-copy of a version -> NEW draft
        |   body: { sourceVersionId?, schemaVersion?, metadata? }
        v   edit the draft copy, then publish it; it supersedes the previous version
```

A published version can never be edited in place. The only way to change an exam's content is to
create a new draft version copied from an existing one, edit it, and publish it (§3.10–§3.12).

`GET /api/sync/pull` and `POST /api/sync/push` require a node-access token
(`token_type = node_access`, non-empty `node_id` claim). The caller's node identity is always the
`node_id` claim of the validated JWT — never the `nodeId` query parameter, the `X-Node-Id` header
or the request body. A valid user/operator token gets `403`; a node token that presents a different
node id gets `403`. The `nodeId` query parameter stays optional for backward compatibility: nodes
already installed send it, and when present it must equal the claim. See §6.

A newly created exam is no longer version-less: it always starts with version `versionNumber = 1`
in status `Draft`. The version is empty, so the exam's computed `publicationState` starts at
`draft`; adding the first block moves it to `ready_to_publish`.

---

## 2. Publication status

`publicationState` is computed per exam (never stored) on `GET /api/exams` and on the create
response:

| `publicationState` | Meaning                                                                 |
| ------------------ | ----------------------------------------------------------------------- |
| `draft`            | No version has any block yet. Nothing can be published.                 |
| `ready_to_publish` | At least one version has `blockCount >= 1`, but no version is published. |
| `published`        | A version was published and a `PublicationPackage` exists.              |

There is no fourth "reaching nodes" state. `pulledByNodeCount` remains available in the API summary
for compatibility and counts nodes that pulled the latest package (see §6); the Central Web UI
does not display it.

Companion fields on `ExamSummaryDto` (all additive; older consumers ignore them):

| Field                    | Type                                  | Notes                                                                 |
| ------------------------ | ------------------------------------- | --------------------------------------------------------------------- |
| `initialVersionId`       | `string?`                             | Only on the create response; the auto-created draft version.          |
| `publicationState`       | `string`                              | `draft` \| `ready_to_publish` \| `published`. Defaults to `draft`.    |
| `publishedVersionId`     | `string?`                             | Latest published version (by `versionNumber`), or null.               |
| `publishedVersionNumber` | `int?`                                | Number of that version, or null.                                      |
| `publishedAt`            | ISO-8601 `string?`                    | `ExamVersion.PublishedAt`, or null.                                   |
| `targets`                | `{targetType,targetId}[]?`            | Descriptive grade/subject/division metadata; new publications do not contain delivery targets. |
| `pulledByNodeCount`      | `int?`                                | Nodes that received the latest package; null if not published.        |

`versionCount` is now `1` immediately after create (it was `0` before this change).

Per-version readiness is exposed on every `ExamVersionDto` (version list and version detail):

| Field                 | Type      | Meaning                                                                 |
| --------------------- | --------- | ----------------------------------------------------------------------- |
| `blockCount`          | `int`     | Number of blocks on the version.                                        |
| `canPublish`          | `bool`    | Not published, has blocks, and no blocker.                              |
| `publishBlockedReason`| `string?` | `already_published` \| `no_blocks` \| null.|

`canPublish = true` means the version is not blocked by its own content. The publish call still
applies question validation and checks that referenced image assets exist, so it is possible for
`canPublish` to be true and publish to return `400`.

Computed publication fields on every `ExamVersionDto` (version list and version detail). They are
derived from the exam's versions, never stored (except the source link):

| Field                   | Type          | Meaning                                                                                      |
| ----------------------- | ------------- | -------------------------------------------------------------------------------------------- |
| `publishedAt`           | ISO-8601 `string?` | `ExamVersion.PublishedAt`, or null while the version is a draft.                         |
| `supersededAt`          | ISO-8601 `string?` | For a published version that has been superseded: the `publishedAt` of the next published version (lowest `versionNumber` greater than this one), clamped so it is never earlier than this version's own `publishedAt`. Null for the current version and for drafts. |
| `isCurrent`             | `bool`        | True only for the latest published version of the exam.                                      |
| `basedOnVersionNumber`  | `int?`        | `versionNumber` of the version this one was copied from; null for an empty/initial version.  |

`ExamSummaryDto.publishedVersionId` / `publishedVersionNumber` / `publishedAt` / `targets` /
`pulledByNodeCount` always describe the **current** (latest published) version, so after publishing
a newer version the summary moves to it automatically.

---

## 3. Endpoints

### 3.1 Create exam — `POST /api/exams`

Request:

```json
{
  "code": "EXA-2026-01",
  "title": "Matemática · Primer Año",
  "description": null,
  "courses": ["secundaria-1"],
  "area": "Matemática",
  "subject": "Números y Operaciones"
}
```

Response `201 Created`:

```json
{
  "id": "ex_abc123",
  "code": "EXA-2026-01",
  "title": "Matemática · Primer Año",
  "courses": ["secundaria-1"],
  "area": "Matemática",
  "subject": "Números y Operaciones",
  "status": "Draft",
  "versionCount": 1,
  "initialVersionId": "ev_def456",
  "publicationState": "draft",
  "publishedVersionId": null,
  "publishedVersionNumber": null,
  "publishedAt": null,
  "targets": null,
  "pulledByNodeCount": null
}
```

Errors: `400` validation (`code`, `title` and at least one valid `course` required, `code` format,
duplicate `code`),
`401` unauthenticated.

### 3.2 List exams — `GET /api/exams`

Response `200`: array of `ExamSummaryDto` as above (without `initialVersionId`, which is null in
list responses).

### 3.3 Create another version — `POST /api/exams/{examId}/versions`

Creates a new draft version with `versionNumber = max(versionNumber) + 1`. Every body field is
optional and so is the body itself: a request with no body (or no `Content-Type`) behaves like `{}`.

```json
{
  "schemaVersion": 1,
  "metadata": { "generatedBy": "teacher01" },
  "sourceVersionId": "ev_def456",
  "empty": false,
  "force": false
}
```

| Field             | Default                                                                                     |
| ----------------- | ------------------------------------------------------------------------------------------- |
| `sourceVersionId` | The exam's highest-`versionNumber` **published** version. When the exam has no published version, the highest-`versionNumber` version overall. An unpublished draft is never copied by default. |
| `schemaVersion`   | The source version's `schemaVersion`, else `1`.                                             |
| `metadata`        | Deep copy of the source version's metadata.                                                 |
| `empty`           | `false`. Set `empty: true` to create an empty version instead of copying (legacy behaviour).|
| `force`           | `false`. Set `force: true` to create the version even when the exam already has a draft.    |

When the source is copied, the new version is a **deep copy**: new ids for the version, question
blocks, block options, answer keys and assets. Each copied question's `config.imageAssetId` is
rewritten to the copied asset id; `sourceVersionId` is stored and exposed as
`basedOnVersionNumber`. The copy starts in status `Draft`, so it is fully editable.

Response `201 Created`: `ExamVersionDto` for the new draft, carrying the copied `blocks`,
`answerKeys` and `assets`, `basedOnVersionNumber`, and readiness fields. An empty copy reports
`blockCount: 0`, `canPublish: false`, `publishBlockedReason: "no_blocks"`.

Errors: `404` unknown exam or unknown `sourceVersionId`; `400` validation, or a `sourceVersionId`
that belongs to a different exam (validation key `sourceVersionId`); `409` concurrent version
creation.

An exam may have at most one draft at a time. When a version in status `Draft` already exists and
`force` is not `true`, nothing is created and the call returns `409` with:

```json
{ "code": "draft_exists", "draftVersionId": "ev_draft789" }
```

`draftVersionId` is the highest-`versionNumber` existing draft, so the client can open it instead of
creating a duplicate. Set `force: true` to create a second draft anyway.

### 3.4 List versions — `GET /api/exams/{examId}/versions`

Response `200`: array of `ExamVersionDto` ordered by `versionNumber` descending, each carrying
`blockCount`/`canPublish`/`publishBlockedReason`. Errors: `404` unknown exam.

### 3.5 Get version — `GET /api/exams/versions/{versionId}`

Response `200`: `ExamVersionDto` with full `blocks`, `answerKeys`, `assets` and readiness fields.
Errors: `404`.

### 3.6 Write the document (canonical builder path) — `PUT /api/exams/versions/{versionId}/document`

Replaces the whole question/answer-key set in one call. Multiple-choice questions carry their
scoring policy in `config.scoringPolicy` only when `config.multiple` is `true`; when absent, grading
uses `AllOrNothing`. True/false questions do not carry a per-question policy. Either question type
may include an `imageAssetId` in `config` to show an image with that question.

Request:

```json
{
  "metadata": { "revision": 7 },
  "blocks": [
    {
      "orderIndex": 0,
      "blockType": "MultipleChoice",
      "title": "Pregunta 1",
      "description": null,
      "config": { "question": "¿Cuánto es 2 + 2?", "multiple": true, "scoringPolicy": "ProportionalPenalised", "imageAssetId": "asset-123", "options": [{ "value": "42", "label": "42" }, { "value": "44", "label": "44" }] },
      "validation": null,
      "correctAnswer": { "value": "42" },
      "scoreValue": 1
    }
  ]
}
```

Response `200`: `ExamVersionDto` with the new blocks and readiness.
Errors: `404`, `409` version already published (published versions are immutable), `400` block
validation.

### 3.7 Upsert one block — `PUT /api/exams/versions/{versionId}/blocks`

Body `UpsertBlockRequest` (`orderIndex`, `blockType`, `title`, `description`, `config`,
`validation`). Also available as `PUT /api/exams/versions/{versionId}/blocks/{orderIndex}`.
Response `200`: `BlockDto`. Errors: `404`, `409` published version, `400` validation.
Supported question types are `MultipleChoice` and `TrueFalse`. `MultipleChoice` requires a
`question` string and at least two `options`; `multiple` controls whether multiple answers can be
selected. `scoringPolicy` is allowed only when `multiple` is `true` and must be one of
`AllOrNothing`, `ProportionalPenalised`, or `ProportionalPlain`. `TrueFalse` requires a `question`
string and does not accept `scoringPolicy`. Both types may include an optional `imageAssetId` that
references an uploaded asset in the same version.

### 3.8 Add an image asset — `POST /api/exams/versions/{versionId}/assets`

Body `CreateAssetRequest` (`fileName`, `mimeType`, `contentBase64`). Only `image/jpeg`, `image/png`
and `image/webp` are accepted. The decoded image must be no larger than 2 MiB. Response `201`:
`AssetDto`. Errors: `404`, `409` published version, `400` invalid payload.

Image bytes can be read with
`GET /api/exams/versions/{versionId}/assets/{assetId}`. It returns the image with its MIME type, or
`404` when the asset does not belong to the version. At publish time, every referenced
`imageAssetId` must exist in that version. Only assets referenced by a question are included in the
published package and its checksum; unreferenced uploads are omitted.

### 3.9 Publish — `POST /api/exams/versions/{versionId}/publish`

Request:

```json
{
  "subject": "Matemática",
  "division": null
}
```

- `grade` tags are derived from the exam's selected courses.
- `subject`, `division` are descriptive metadata (see §4).
- Legacy `nodeIds` and `schoolIds` request fields are deprecated, accepted, and ignored. Every
  published exam is delivered to every node.

Response `200`:

```json
{
  "packageId": "pkg_789",
  "examVersionId": "ev_def456",
  "packageVersion": 1,
  "checksum": "sha256-...",
  "targets": [
    { "targetType": "grade", "targetId": "primaria-6" },
    { "targetType": "subject", "targetId": "Matemática" }
  ]
}
```

Effects: creates `publication.packages` row with `Status = "Published"`, writes
`publication.targets` for descriptive grade/subject/division metadata only, and sets the version
status to `Published` with `publishedAt`. Published versions become immutable and can only be
published once. New publishes do not write node or school delivery targets.

Publishing runs as one database transaction. A unique constraint permits one package per exam
version, so simultaneous publish requests can produce one `200` response and one `409` response;
the losing request receives the same already-published conflict as a later retry.

Errors: `404` unknown version, `409` already published, `409` when the version's `versionNumber` is
lower than the current published version's (out-of-order publish, body
`{ "code": "older_than_current" }`, nothing changes), `400` with a `ValidationProblemDetails`
whose error keys are `blocks` (no blocks) or per-block/config keys; `400` when
a question's `imageAssetId` references an asset that does not exist in the version.

### 3.10 Edit exam metadata — `PUT /api/exams/{examId}`

Updates the mutable metadata of an exam. The `code` is immutable.

Request:

```json
{
  "code": "EXA-2026-01",
  "title": "Matemática · Primer Año (revisado)",
  "description": "Segunda edición",
  "courses": ["secundaria-1"],
  "area": "Matemática",
  "subject": "Números y Operaciones"
}
```

- `title` is required; `description` / `courses` / `area` / `subject` are optional and replace the
  stored value.
- `code` is optional. Omitted (or equal to the stored code) keeps it; present and different from the
  stored code is a `400` with validation key `code`.

Response `200`: `ExamSummaryDto` with the same computed publication fields as `GET /api/exams`.

Errors: `404` unknown exam, `400` validation (same `ExamValidator` as create: `title` non-empty and
max 256, immutable `code`), authorization identical to `POST /api/exams` (controller-level
`[Authorize]`).

### 3.11 Edit-as-new-version flow (published exams)

A published version is immutable: `PUT .../document`, `PUT .../blocks` and `POST .../assets` return
`409`. To change a published exam:

1. `POST /api/exams/{examId}/versions` with `{ "sourceVersionId": "<current published version id>" }`
   (or no body at all, which defaults to the highest published version) to get an editable draft
   deep-copy.
2. Edit the draft with the normal `document` / `blocks` / `assets` endpoints.
3. `POST /api/exams/versions/{draftVersionId}/publish` to emit a new package.

The API also allows `PUT /api/exams/{examId}` at any time to fix metadata without a new version.

### 3.12 Superseding semantics

Publication is append-only: publishing never mutates or deletes an earlier package.

- On publish, the version's `status` becomes `Published` and `publishedAt` is set.
- The **current** version is the published version with the highest `versionNumber`; only it reports
  `isCurrent: true`.
- Every earlier published version reports `isCurrent: false` and `supersededAt` = the `publishedAt`
  of the next published version, clamped with `max` against its own `publishedAt` so a clock-skewed
  next publication can never claim to have superseded it before it was published.
- Publishing a version whose `versionNumber` is lower than the current published version's returns
  `409 { "code": "older_than_current" }` and changes nothing.
- `ExamSummaryDto.publishedVersion*` points at the current version.
- Previous `PublicationPackage` rows stay `Published` and are not deleted, so a node that has not
  pulled the older package yet can still receive it; the newer package is delivered afterwards
  because `/api/sync/pull` orders candidates by `publishedAt` (see §5).

---

## 4. Publication metadata and delivery

| `targetType` | Meaning |
| ------------ | ------- |
| `grade`      | Course tags derived from the exam’s selected `courses`. Descriptive metadata. |
| `subject`    | Subject. Descriptive metadata. |
| `division`   | Division. Descriptive metadata. |
| `node`       | Legacy target retained on packages already published. |
| `school`     | Legacy target retained on packages already published. |

Every published package is delivered to every node, including packages that already have legacy
`node` or `school` target rows. Sync pull ignores those stored rows when deciding delivery. Existing
target rows and checksums are not rewritten. Grade, subject and division remain package metadata.

## 5. Cursor semantics

`pull` returns `nextCursor` as the published-at ticks of the last package in the page. Every
published package in that page is delivered.

`hasMore = true` means more candidates exist past the returned cursor; the node should call again
immediately. Nodes pass `nextCursor` back verbatim.

The cursor key is opaque to the node. Old `cursor` values that are plain tick numbers keep working
(`ParseCursor` treats an unparsable cursor as `0`).

---

## 6. How a node receives an exam

Node-side pieces (Local API; implemented outside this Central change):

1. **Autonomous sync.** `SyncBackgroundService` runs every ~30 s while no delivery session is
   active. It probes `{centralUrl}/health/live`, then calls
   `GET {centralUrl}/api/sync/pull?nodeId={nodeId}&cursor={last_exam_pull_cursor}&limit=50`,
   imports every item with `entityType == "publication_package"` and `operation == "upsert"`, and
   stores `nextCursor` in `sync_state.last_exam_pull_cursor`.
2. **On-demand pull.** `POST /api/sync/pull-exams` on the Local API runs the same pull immediately
   (diagnostics / operator override). It returns `LocalExamPullResult`.

The package payload (`PublishedExamPackageDto`) contains `packageId`, `examId`, `examVersionId`,
`examCode`, `title`, `versionNumber`, `schemaVersion`, `checksum`, `metadata`, `blocks`,
`answerKeys`, `assets` (base64), `targets`, and the legacy top-level `scoringPolicy` set to
`AllOrNothing`. That field keeps older Local nodes able to grade multiple-choice exams; remove it
after all deployed Local versions support per-question policies. Current grading uses each
multiple-choice block's `config.scoringPolicy`, which travels with the block. The legacy field is
compatibility metadata and is not part of the package checksum.

### 6.1 Node identity binding (D6)

`SyncController` resolves the node id with `NodeAccessAuth.TryGetNodeId(User, out var nodeId)` and
uses that claim for identity and `RecordPullProgressAsync` (the `(nodeId,
"package:{packageId}")` delivery markers that feed `pulledByNodeCount`). Consequences:

- A user/operator bearer token (`token_type != node_access`) is rejected with `403` on both
  `pull` and `push` — the token is valid, just not privileged for node sync.
- A node token cannot impersonate another node: if `nodeId` (pull) or the `X-Node-Id` header /
  `request.NodeId` (push) is present and differs from the claim, the call is rejected with `403`.
  The claim value is used even when the client sends no id.
- On push, the pre-existing `400` for a missing `X-Node-Id` or a header/body mismatch is kept and
  evaluated only after the token is confirmed to be a node-access token.
- `sync.cursors` rows (exam-pull cursor and delivery markers) are therefore always written under
  the authenticated claim, never under a spoofed id.

`pulledByNodeCount` on `ExamSummaryDto` is computed from Central's `sync.cursors` table: the pull
endpoint upserts one marker per delivered package keyed `(nodeId, "package:{packageId}")`, and the
count is the number of such markers for the latest published package. It is `null` when the exam has
no published package, and `0` when published but not yet pulled.

---

## 7. Storage touched

- `publication.packages` — one row per publish (`Status = "Published"`, checksum, manifest).
- `publication.targets` — target rows (`TargetType`, `TargetId`).
- `sync.cursors` — `(NodeId, CursorKey, CursorValue)`; `CursorKey = "exam_pull"` for the scan cursor
  and `CursorKey = "package:{packageId}"` for delivery markers.
- `exams` / `exam_versions` / `exam_blocks` / `exam_answer_keys` / `exam_assets` — authoring data;
  a version's `status` becomes `Published` and `published_at` is set at publish time.
- `exam.versions.source_version_id` — nullable column added by the
  `AddExamVersionSourceVersion` EF migration. It stores the version a draft copy was created from;
  `basedOnVersionNumber` is resolved from it at read time.

Registered nodes receive every school/year roster through `/api/sync/rosters/index` and
`/api/sync/roster/{cue}/{schoolYear}`. Enrollment CUE does not restrict roster delivery.

The `publishedAt` / `supersededAt` / `isCurrent` / `basedOnVersionNumber` DTO fields are computed on
read; only `source_version_id` is persisted by this change.
