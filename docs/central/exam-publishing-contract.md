# Central exam publishing contract

Audience: the web-UI leader building the authoring screens, and any node-side integrator.
Scope: the Central API endpoints that take an exam from creation to a package that installed nodes
receive. All paths are relative to the Central API base URL. All endpoints require
`Authorization: Bearer <access token>` unless noted. JSON uses camelCase; `blockType` is serialized
as a string (e.g. `"TrueFalse"`).

Source of truth: `src/Central/PlanCope.Central.Api/Controllers/ExamsController.cs`,
`src/Central/PlanCope.Central.Api/Controllers/SyncController.cs`,
`src/Shared/PlanCope.Shared.Contracts/Exams/ExamContracts.cs`.

---

## 1. Full sequence

```
POST /api/exams                                  create exam + initial empty draft version
        |
        v   examSummary.initialVersionId
PUT  /api/exams/versions/{versionId}/document    (builder) replace blocks + scoring policy
  or PUT /api/exams/versions/{versionId}/blocks  (single block upsert)
        |
        v   version is READY when blockCount >= 1 (and scoring policy set if any MCQ)
POST /api/exams/versions/{versionId}/publish     emit PublicationPackage (Status=Published)
        |
        v
GET  /api/sync/pull?nodeId=&cursor=&limit=       node receives the package
```

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

There is no fourth "reaching nodes" state. Delivery is observable separately: `pulledByNodeCount`
counts how many nodes actually received the latest published package (see §6), and the node's own
`/api/sync/status` reports its last pull time.

Companion fields on `ExamSummaryDto` (all additive; older consumers ignore them):

| Field                    | Type                                  | Notes                                                                 |
| ------------------------ | ------------------------------------- | --------------------------------------------------------------------- |
| `initialVersionId`       | `string?`                             | Only on the create response; the auto-created draft version.          |
| `publicationState`       | `string`                              | `draft` \| `ready_to_publish` \| `published`. Defaults to `draft`.    |
| `publishedVersionId`     | `string?`                             | Latest published version (by `versionNumber`), or null.               |
| `publishedVersionNumber` | `int?`                                | Number of that version, or null.                                      |
| `publishedAt`            | ISO-8601 `string?`                    | `ExamVersion.PublishedAt`, or null.                                   |
| `targets`                | `{targetType,targetId}[]?`            | Target scope of the latest published package. `[]` = all nodes.       |
| `pulledByNodeCount`      | `int?`                                | Nodes that received the latest package; null if not published.        |

`versionCount` is now `1` immediately after create (it was `0` before this change).

Per-version readiness is exposed on every `ExamVersionDto` (version list and version detail):

| Field                 | Type      | Meaning                                                                 |
| --------------------- | --------- | ----------------------------------------------------------------------- |
| `blockCount`          | `int`     | Number of blocks on the version.                                        |
| `canPublish`          | `bool`    | Not published, has blocks, and no blocker.                              |
| `publishBlockedReason`| `string?` | `already_published` \| `no_blocks` \| `scoring_policy_required` \| null.|

`canPublish = true` means the version is not blocked by its own content. The publish call still
applies request-level gates (`grade` required, block validation, referenced image assets must
exist), so it is possible for `canPublish` to be true and publish to return `400`.

---

## 3. Endpoints

### 3.1 Create exam — `POST /api/exams`

Request:

```json
{
  "code": "EXA-2026-01",
  "title": "Matemática · Primer Año",
  "description": null,
  "level": "Secundario",
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
  "level": "Secundario",
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

Errors: `400` validation (`code`/`title` required, `code` format, duplicate `code`),
`401` unauthenticated.

### 3.2 List exams — `GET /api/exams`

Response `200`: array of `ExamSummaryDto` as above (without `initialVersionId`, which is null in
list responses).

### 3.3 Create another version — `POST /api/exams/{examId}/versions`

Only needed when authoring a second (or later) version of an exam. The initial version already
exists after create.

Request:

```json
{ "schemaVersion": 1, "metadata": { "generatedBy": "teacher01" }, "scoringPolicy": null }
```

Response `201 Created`: `ExamVersionDto` (empty `blocks`, `blockCount: 0`,
`canPublish: false`, `publishBlockedReason: "no_blocks"`).
Errors: `404` unknown exam, `400` validation, `409` concurrent version creation.

### 3.4 List versions — `GET /api/exams/{examId}/versions`

Response `200`: array of `ExamVersionDto` ordered by `versionNumber` descending, each carrying
`blockCount`/`canPublish`/`publishBlockedReason`. Errors: `404` unknown exam.

### 3.5 Get version — `GET /api/exams/versions/{versionId}`

Response `200`: `ExamVersionDto` with full `blocks`, `answerKeys`, `assets` and readiness fields.
Errors: `404`.

### 3.6 Write the document (canonical builder path) — `PUT /api/exams/versions/{versionId}/document`

Replaces the whole block/answer-key set in one call. Setting `scoringPolicy` here is how the UI
chooses the policy required to publish an exam that contains multiple-choice blocks.

Request:

```json
{
  "metadata": { "revision": 7 },
  "scoringPolicy": "ProportionalPenalised",
  "blocks": [
    {
      "orderIndex": 0,
      "blockType": "MultipleChoice",
      "title": "Pregunta 1",
      "description": null,
      "config": { "question": "¿Cuánto es 2 + 2?", "options": [{ "value": "42", "label": "42" }, { "value": "44", "label": "44" }] },
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
Type-specific `config` requirements: `Text` needs `content`; `MultipleChoice` needs `question` and
`options` (>= 2); `TrueFalse` needs `question`; `ShortAnswer` needs `prompt`; `Image` needs
`assetId`.

### 3.8 Add an image asset — `POST /api/exams/versions/{versionId}/assets`

Body `CreateAssetRequest` (`fileName`, image `mimeType`, `contentBase64`). Response `201`:
`AssetDto`. Errors: `404`, `409` published version, `400` invalid payload.

### 3.9 Publish — `POST /api/exams/versions/{versionId}/publish`

Request:

```json
{
  "subject": "Matemática",
  "grade": "6",
  "division": null,
  "nodeIds": ["node_a1"],
  "schoolIds": ["1001"]
}
```

- `grade` is required.
- `subject`, `division` are descriptive metadata (see §4).
- `nodeIds` / `schoolIds` are optional delivery filters. Omit both (or send empty arrays) for
  "all nodes".

Response `200`:

```json
{
  "packageId": "pkg_789",
  "examVersionId": "ev_def456",
  "packageVersion": 1,
  "checksum": "sha256-...",
  "targets": [
    { "targetType": "grade", "targetId": "6" },
    { "targetType": "node", "targetId": "node_a1" },
    { "targetType": "school", "targetId": "1001" }
  ]
}
```

Effects: creates `publication.packages` row with `Status = "Published"`, writes
`publication.targets`, and sets the version status to `Published` with `publishedAt`. Published
versions become immutable and can only be published once.

Errors: `404` unknown version, `409` already published, `400` with a `ValidationProblemDetails`
whose error keys are `blocks` (no blocks), `scoringPolicy` (MCQ without policy), `grade` (missing),
or per-block/config keys; `400` when an `Image` block references an asset that does not exist.

---

## 4. Targeting semantics

Target types:

| `targetType` | Delivery filter? | Meaning                                                        |
| ------------ | ---------------- | -------------------------------------------------------------- |
| `grade`      | No               | Course/grade the exam is for. Descriptive metadata.            |
| `subject`    | No               | Subject. Descriptive metadata.                                 |
| `division`   | No               | Division. Descriptive metadata.                                |
| `node`       | **Yes**          | `targetId` is a registered node id.                            |
| `school`     | **Yes**          | `targetId` is a School id **or** a school CUE.                 |

Decision (verified against `BuildTargets` in `ExamsController.cs`): publish always writes at least a
`grade` target, so "a package with targets is filtered" cannot mean every target type filters —
otherwise no package would ever reach a node. Therefore only `node` and `school` are treated as
delivery filters. `grade`/`subject`/`division` describe the exam (the node stores them as metadata
when it imports the package) but never restrict which nodes receive it.

Delivery rules in `GET /api/sync/pull`:

- A package with **no** `node`/`school` targets is delivered to **every** node, including a node id
  that Central does not know (an unenrolled or not-yet-roster node).
- A package with at least one `node`/`school` target is delivered **only** to a node that matches at
  least one of them.
- `node` matches when `targetId` equals the requesting `nodeId` exactly.
- `school` matches when `targetId` equals any of the node's school identifiers: the node's
  `RegisteredNode.SchoolId` (if set), the node's enrolment `Cue`, or the internal `School.Id` whose
  `Cue` equals the node's CUE. Nodes enrol by CUE, so both the School id and the CUE are accepted.

---

## 5. Cursor semantics

`pull` returns `nextCursor` as the published-at ticks of the **last candidate examined** in the
page — not only of the last item delivered. This is deliberate:

- Packages skipped because the node is not a target still advance the cursor, so a non-targeted node
  does not rescan them forever.
- Candidates are examined in `(publishedAt, id)` order and the cursor only ever advances to the end
  of the examined page, so a package the node must receive can never be jumped over.

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
`answerKeys`, `assets` (base64), `targets`, `scoringPolicy`.

### 6.1 Node identity binding (D6)

`SyncController` resolves the node id with `NodeAccessAuth.TryGetNodeId(User, out var nodeId)` and
uses that claim for targeting, `ResolveNodeSchoolIdsAsync` and `RecordPullProgressAsync` (the
`(nodeId, "package:{packageId}")` delivery markers that feed `pulledByNodeCount`). Consequences:

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

No schema migration is required by this change: all new fields are computed or stored in existing
tables.
