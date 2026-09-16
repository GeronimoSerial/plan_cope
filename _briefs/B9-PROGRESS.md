# B9 · Fail-path UX and end-to-end hardening — progress

Leader: Sonnet (Level-2). Implementer: `opencode run --auto -m opencode-go/deepseek-v4-flash`.
This leader wrote no production code — every change below was dispatched, reviewed against its
diff, built and tested independently before being committed. The only direct edits by this
leader are named explicitly where they occur: one-line mechanical fixes made after full
diagnosis, matching the precedent set by B0/B2/B3 (a leader may fix a fully-diagnosed,
no-design-decision defect directly rather than spend a dispatch round-trip on it).

## Task status

| # | Task | Status |
|---|---|---|
| 1 | DNI-miss reframed as a neutral question, no-name-leak re-verified by test | DONE |
| 2 | Audit of user-facing failure strings | DONE (scoped — see below) |
| 3 | Offline vs broken distinction surfaced in the operator UI | DONE |
| 4 | Full-system E2E | DONE — both scenarios green, two real production defects found and fixed along the way |
| 5 | Coverage for ActivationKeyStore, five validators, student exam-taking UI | DONE (substantial, not exhaustive — see below) |
| 6 | Documentation reconciliation | DONE (commit `41afc99`) |
| 7 | Harden `LoginResponse` | DONE |

## Task 1 — DNI-miss no-leak (commit `23fc11c`)

`AttemptEndpoints.cs`'s 404 for an unmatched DNI now returns `{ kind: "not_found", message,
hint }` instead of `{ error: "..." }`; the frontend (`SessionEntryPanel.tsx` via a new
`StudentNotFoundError` thrown specifically for this case in `studentApi.ts`) renders it as
neutral copy, never inside the red `error-banner`/`role="alert"` treatment used for every other
error. The regression test (`Student_resolution_miss_leaks_no_student_name`) seeds real student
names into three different no-match scenarios (unknown DNI, DNI valid but in another section,
DNI valid but in another school) and asserts the response body contains NONE of the actual
seeded names — a guarantee that cannot pass by accident, unlike a regex or an eyeball check.

## Task 7 — LoginResponse hardening (commit `011d7ea`)

`LoginResponse.AccessToken` is now `[property: JsonRequired]`. A missing `accessToken` now
throws `JsonException` during deserialization instead of silently producing `AccessToken =
null`. The pinning test was renamed (not just edited) from
`LoginResponse_missing_access_token_becomes_null` to
`LoginResponse_missing_access_token_throws`, with a comment stating the change is deliberate —
a test whose name still said `_becomes_null` while asserting a throw would have been a trap for
the next reader, not just a stale pin.

## Task 3 — offline vs broken (commit `eba6b79`)

B5 already built the `offline`/`lastError` distinction into `/api/sync/status`; nothing in the
operator UI consumed that endpoint at all before this batch. Added `SyncStatusIndicator` +
`useSyncStatus` (30s poll, visibility-gated, keeps last-known-good state on a transient poll
failure so a flaky poll never itself looks like a sync failure), wired into `AppShell`'s footer
next to `UpdateStatus`. Three states, textually and visually distinct: offline (calm, "se
sincronizará al reconectar", never the word "error"), a real post-connection failure (the only
state allowed to read as a problem), and healthy.

## Task 2 — copy audit (commit `01e9a6b`)

Scoped deliberately, not exhaustively: fixed every English string in `AttemptEndpoints.cs` and
`SessionEndpoints.cs` that a real student or operator can actually see in the exam-delivery flow
(session-not-found, attempt-not-found, inactive-session, invalid-transition — all reworded to
neutral, actionable Spanish matching this codebase's existing tone). Left `RosterEndpoints.cs`,
`StatsEndpoints.cs`, `SyncEndpoints.cs`, `ExamEndpoints.cs` untouched — their remaining English
strings are query-parameter guards that only fire on a malformed request already validated
client-side, not reachable through a real user flow. This is a judgment call, not an oversight;
revisit if any of those endpoints ever gain a direct un-validated caller.

## Task 5 — test coverage (commits `7f7bc1d`, `7f4c903`, `3680f4c`)

- **ActivationKeyStore**: guard clauses and preconditions only (`HasStoredKey`, `Store` null/empty
  rejection, `Load` on an unactivated machine). The real DPAPI encrypt/decrypt round-trip cannot
  run here — `ProtectedData.Protect` throws `PlatformNotSupportedException` on Linux — and no
  test pretends otherwise. Needs a real Windows machine to close.
- **All five FluentValidation validators**: full coverage, including the two most complex ones
  (`ExamBlockValidator`'s per-`BlockType` config rules, `CreateSessionRequestValidator`'s
  three-fields-travel-together nominal rule).
- **Student exam-taking UI**: pure domain logic (`examAnswers.ts`, `examBlocks.ts`) and two
  previously-untested components (`StudentIdentityConfirmationPanel`, `ExamConfirmationPanel`)
  now have real rendered-output coverage, on top of Task 1's `SessionEntryPanel` test and B8's
  existing `ExamTakingPanel` virtualization test. **Not exhaustive**: `QuestionNav.tsx`,
  `QuestionTitle.tsx`, `SubmitConfirmDialog.tsx`, `ExamBlock.tsx` and `studentApi.ts` itself
  still have no dedicated test. This project has no `@testing-library/react` or click-simulation
  harness at all (checked before every dispatch, not assumed) — adding one is a new-dependency
  decision reserved for the coordinator, per B2's own precedent, not something to add
  unilaterally mid-batch. What's covered closes the plan's named audit finding; what's left is a
  smaller, lower-risk remainder for whoever picks this up next.

## Task 4 — full-system E2E: DONE, both scenarios green (commits `120e00e`, `5c256b3`, `8dc0201`)

Commit `e007dea` (WIP, honestly labelled) has the scenario structure in place:
activate offline (real encrypted roster bundle, real Phase A `/unlock`) → initial catalog pull
while connected → go offline → run a session → grade → read stats while still offline → reconnect
→ push the outbox → verify on Central, including comparing Central's independently recomputed
`CentralAttemptResult` against Local's own `attempt_results` row (the §7 grading-determinism
gate, proven end-to-end here, not argued) → gated-update feed (server side only) → verify data
survived.

**What this scenario taught me about how the eight batches actually fit together — recorded here
because it is not visible from any single batch's diff:**

1. **The connectivity boundary is narrower than "does the test call Central."** Proving "this ran
   offline" by argument (as B4 and B5 both had to do, for lack of a way to test it) is weaker than
   proving it by making the Central-bound HTTP handler *throw* during the offline window and
   confirming the scenario still succeeds. Building that (`SwitchableHandler`, a real
   `HttpMessageHandler` that throws or delegates to Central's in-process `TestServer` handler
   depending on a boolean anyone can flip) turned a documented assumption into an executable one.
   Worth pulling out of this test file into a shared E2E test-support type if a future batch needs
   the same proof — right now it's a private nested class, duplicating nothing yet only because
   nothing else needs it yet.
2. **Stats rollups are scoped to nominal sessions in a way the plan's task-4 wording does not
   surface.** "Run session → grade → compute stats" reads as one continuous capability. It is not:
   `StatsRollupRepository.UpsertForAttemptAsync` (B4) silently no-ops for any session that isn't
   roster-linked (`SchoolYear`/`RosterSectionId` both required). A non-nominal session — the kind
   every other test in this repo, including the original E2E scenario, uses for simplicity —
   never produces a rollup row, ever, by design. The first draft of this scenario used the
   existing non-nominal `RunSessionAndSubmitAsync` helper and the stats leg silently had nothing
   to show. This is not a bug; it is a real product decision (statistics are a roster-scoped
   concept) that no batch's own progress notes stated as plainly as "a non-nominal session will
   never appear in statistics." Anyone building an operator-facing "why don't I see this session
   in my stats" explanation should know this is the reason, not a data-sync bug.
3. **This same investigation found a real, separate production defect**, distinct from anything
   about this test's own design: `GET /api/stats/course` and `GET /api/stats/exam` throw an
   unhandled 500 — not a clean empty result — whenever their `GROUP BY` matches zero rows (e.g. a
   school with valid data for other courses but none yet for the one requested, or, as here, a
   cue with zero nominal graded attempts so far). Root cause: a bare SQL aggregate expression
   (`SUM(x) AS X`) has no declared type for SQLite to report when there are no rows to sample,
   and `Microsoft.Data.Sqlite` falls back to reporting the column as `byte[]`, which breaks
   Dapper's strongly-typed constructor materialization. `GET /api/stats/school` does not have
   this bug — it wraps its aggregates in `COALESCE(SUM(...), 0)` with no `GROUP BY`, so it always
   returns exactly one row. This is precisely the failure class B4-PROGRESS already named once
   ("a green build proves nothing about a runtime type-materialization mismatch; only a test that
   runs the actual query does") — recurring here because the *specific* empty-result shape had
   never been exercised, not because the earlier fix was wrong. **The obvious fix does not
   work**: wrapping every `SUM(x)` in `CAST(x AS INTEGER)` was tried first and does nothing —
   proven wrong with a standalone Microsoft.Data.Sqlite 8.0.6 repro run directly in this
   environment (not assumed): `GetFieldType()` reports `byte[]` for a zero-row aggregate
   expression regardless of any CAST around it, because `sqlite3_column_decltype()` only reflects
   direct column references, never computed expressions. The real fix checks for at least one
   matching row first, with a query that has no aggregate columns (`SELECT DISTINCT course ...`),
   and only runs the full aggregate query once that is known to return something — a query Dapper
   can always materialize correctly, zero rows or not, because it's a plain string column. A
   regression test (`StatsEmptyResultTests.cs`) proves both `/course` and `/exam` now return
   `200 []`, not a 500, over an empty rollup table. This is the kind of finding the plan's own
   words anticipated for this task ("expect it to be hard; the
   difficulty is the point") — it would not have surfaced from any single batch's own test suite,
   because no batch's own scope included "a school with zero nominal attempts so far," which is
   in fact the most common real-world state for a brand-new school on day one.
4. **The seam between Local and Central is sharper than the plan implies for gated updates.**
   B7's own progress log already named this precisely (`_briefs/B7-PROGRESS.md`, "Known gap, not
   closed in this PR"): nothing writes to `sync.release_rings` over HTTP. This scenario seeds a
   `RegisteredNode` and a `ReleaseRing` directly via `PlanCopeDbContext`, named explicitly as a
   substitute for an admin-issuance endpoint that does not exist yet. That substitution proves the
   feed's SERVER side for a real registered node over real HTTP (the exact wire shape B7 verified
   against pinned Velopack 0.0.1251) but proves nothing about a client downloading, verifying,
   applying, or restarting — that needs a real Windows machine and a `vpk pack`-produced
   installer, which cannot exist in this environment by construction. No assertion in this test
   claims otherwise; per the coordinator's explicit standard, a "did not throw" assertion around
   Velopack's client behavior would pass identically on a machine with no update mechanism
   installed at all, which is exactly this machine — so no such assertion exists here.
5. **Two regressions this scenario itself introduced into the pre-existing E2E test, both fixed
   before commit**: `CentralApiFactory.DbName` was a `static readonly` field — harmless with one
   `[Fact]` in the class, silently shares one InMemory database across every instance once a
   second `[Fact]` exists. And the new connectivity switch defaulted to offline, which broke the
   original test (which never touches the switch and expects a working connection throughout) —
   fixed by defaulting to online and having only the new test manage both states explicitly.
   Recording both here because a future reader diffing this commit might otherwise assume the
   original test was untouched by this work; it was, functionally, but its supporting
   infrastructure was shared and had to be made safe for two tenants.
6. **A second real production defect, more serious than the first**: `UpdatesController`
   (B7) has depended on `IReleaseGateService` since it was written, and nothing registers
   `ReleaseGateService` in Central's DI container — `Program.cs` never had the line. Every real
   call to `GET /api/updates/releases.{channel}.json` has thrown a 500 in the actual running app
   since B7 shipped, not just in a test. B7's own `UpdatesControllerTests.cs` never caught this
   because it constructs the controller directly with a hand-supplied fake `IReleaseGateService`,
   never resolving it from the real container the way a live request does. This scenario is the
   first thing in the whole project to call this endpoint through a real DI-resolved controller —
   which is exactly why it caught what eight batches of unit tests, each internally consistent,
   could not. Fixed with a one-line registration, `_briefs/B9-PROGRESS.md`-worthy on its own: the
   gated-update feature was entirely non-functional in production before this fix, regardless of
   how well-tested its individual pieces were.

Both defects (5 and the stats-crash one, item 3 above) share the same shape: correct, well-tested
components that had never been wired together and exercised as one running system. That is
precisely what no other batch's own scope could have caught, and precisely why the plan calls
this task the one place the whole system is exercised as one piece.

## Final evidence table — PROVEN / PROVEN AS CODE PATH / NOT PROVEN

Using B7-PROGRESS.md's own three-way distinction, because collapsing "we ran it," "we read it,"
and "we argued it" into one word is exactly the failure this table exists to avoid.

| Claim | Status | Why |
|---|---|---|
| DNI-miss reveals no student name, across unknown/wrong-section/wrong-school documents | PROVEN | Regression test seeds real names and asserts the response body contains none of them, not a pattern match |
| DNI-miss renders as neutral copy, never the error banner | PROVEN | Rendered-markup test asserts absence of `error-banner`/`role="alert"` for this state and presence for every other error |
| `LoginResponse` with no access token fails deserialization | PROVEN | Test deserializes the exact payload through the real source-generated JSON context and asserts the throw |
| Offline vs. sync-error are visually and textually distinct in the operator UI | PROVEN | Rendered-markup tests assert the offline case never contains "error" and the error case is textually distinct from the offline case |
| Local statistics compute correctly for a real nominal graded attempt, entirely offline | PROVEN | The full-system E2E throws if any Central call happens during this window (a real `HttpMessageHandler` that throws when flagged offline, not an argument), and asserts the actual computed `attemptCount` |
| Grading determinism (Local and Central agree on the same attempt) | PROVEN | The E2E compares Local's `attempt_results` row against Central's independently recomputed `CentralAttemptResult` for the same attempt, not two copies of the same code path |
| The gated-update feed serves a real registered node the correct Velopack wire shape | PROVEN | The E2E calls the real endpoint through the real DI container with a real node-access JWT and asserts the exact response shape |
| ActivationKeyStore guard clauses (null/empty key, load-before-activation) | PROVEN | Direct unit tests, no DPAPI involved |
| All five FluentValidation validators | PROVEN | Full positive/negative coverage per rule, including the two multi-field/type-specific ones |
| Student exam-taking domain logic (`examAnswers.ts`, `examBlocks.ts`) and two confirmation panels | PROVEN | Rendered-output / pure-function tests |
| A revoked node drains its outbox before wiping, never mid-session | PROVEN AS CODE PATH | B2's own tests prove the local stage machine against a scripted fake HTTP layer; never exercised against a live Central or a real multi-hour revocation window (B2-PROGRESS's own caveat, unchanged by this batch) |
| Rollback re-applies a known-good package via Velopack | PROVEN AS CODE PATH | B7's 7 unit tests prove the state machine; `ApplyUpdatesAndRestart` itself is unreachable without a real Velopack-installed app |
| Session-gate re-check immediately before restart | PROVEN AS CODE PATH | The call path is structurally forced through the re-check; never exercised end to end with a running host |
| A client actually downloads, verifies, applies or restarts an update | NOT PROVEN, BY CONSTRUCTION | Needs a real Windows machine and a `vpk pack`-produced installer; neither exists here. No assertion in this batch's E2E claims otherwise — a "did not throw" assertion around Velopack's client would pass identically on a machine with no update mechanism installed at all, which is exactly this one |
| `%LocalAppData%\PlanCope\` survives a real Velopack binary swap and restart | NOT PROVEN, BY CONSTRUCTION | This batch's E2E proves data survives the activate/session/grade/stats/sync/update-check sequence at the data level (re-queried after every step); it does not and cannot prove survival across an actual installed-app update, which needs the same missing Windows machine |
| DPAPI round-trip for `ActivationKeyStore.Store`/`Load` | NOT PROVEN, BY CONSTRUCTION | `ProtectedData.Protect` throws `PlatformNotSupportedException` on Linux; only guard clauses are testable here |
| `QuestionNav.tsx`, `QuestionTitle.tsx`, `SubmitConfirmDialog.tsx`, `ExamBlock.tsx`, `studentApi.ts`'s remaining branches | NOT PROVEN, NOT ATTEMPTED | Named as the smaller remainder of task 5's audit finding, not silently dropped — this project has no click-simulation test harness, and adding one is a new-dependency decision for the coordinator, not this batch |
| Idle CPU, cold start, real-hardware Argon2id timing | NOT PROVEN, BY CONSTRUCTION | Unchanged from B8; needs the 2-core/4GB/HDD reference machine named in `docs/reference-profile.md` |

## Task 6 — documentation reconciliation: DONE (commit `41afc99`)

- `docs/activation-passphrase.md` and `README.md` described only Phase A (passphrase/DPAPI) —
  both now describe Phase B (activation key, node enrolment, drain-then-lock revocation) as it
  actually shipped in B1/B2, without deleting or contradicting anything already correct about
  Phase A.
- `PROJECT-CLOSURE-PLAN.md`'s B7 task 3 still described a generic authenticated JSON feed. Added
  a correction note directly under the original bullet (matching §1.3's existing correction
  style — the original plan text stays visible, the correction is dated and attributed) recording
  the real Velopack wire protocol: the fixed `releases.{channel}.json` route, node identity from
  the JWT's `node_id` claim rather than a query parameter, and the PascalCase `VelopackAssetFeed`
  shape — all verified against pinned Velopack 0.0.1251 in B7-PROGRESS.md.
- `docs/REMAINING-WORK.md`'s migration-renumbering note covered only B3/B4; added B2's own
  `009_NodeIdentity.sql` → `010` → `012` renumbering, both collisions caught before merge.
- §1.3 (decision-1 resolution) and §2.2 (two-phase activation) themselves were left untouched —
  both already state the resolved decision correctly; what needed reconciling was everywhere
  else in the docs that still described only half of what §2.2 actually specifies.

## Closing note

All seven tasks are done, all tests are green (424 .NET tests, 85 frontend tests, full solution
build clean with `-warnaserror`), and every commit is pushed to `feat/b9-closing`. This batch
found and fixed two real, previously-invisible production defects — the stats empty-result
crash and the gated-update feed's missing DI registration — neither of which any prior batch's
own test suite could have caught, because both required the whole system running together, which
is exactly what task 4 exists to force. Ready to open the PR.
