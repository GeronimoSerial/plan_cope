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
| 4 | Full-system E2E | IN PROGRESS — see below, this is the hard one |
| 5 | Coverage for ActivationKeyStore, five validators, student exam-taking UI | DONE (substantial, not exhaustive — see below) |
| 6 | Documentation reconciliation | NOT STARTED |
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

## Task 4 — full-system E2E: IN PROGRESS, not yet green

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
   never been exercised, not because the earlier fix was wrong. Fix in progress: explicit
   `CAST(... AS INTEGER)` / `CAST(... AS REAL)` on every aggregate column, plus a regression test
   proving the endpoints return `200 []`, not a 500, over an empty rollup table. This is the kind
   of finding the plan's own words anticipated for this task ("expect it to be hard; the
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

**Remaining before this task is done**: land the `CAST` fix + regression test in
`StatsQueryRepository.cs`, switch the new scenario's session to nominal (using the same roster
bundle already built for the offline-activation leg, so the same seeded student who unlocks
Phase A is the one who takes the exam — narrative coherence, not just a workaround), get both
E2E tests green, and write the final PROVEN / PROVEN AS CODE PATH / NOT PROVEN table using
B7-PROGRESS's own three-way distinction rather than collapsing "we ran it," "we read it," and
"we argued it" into one word.

## Task 6 — documentation reconciliation: NOT STARTED

Last, per the plan's own ordering. Known corrections to make (not an exhaustive list yet):
§1.3's decision-1 resolution (one universal `.exe`, per 1.3 as amended), §2.2's two-phase
activation model, Local migration renumbering collisions (B3's `010`→`011`, B4's `011`→`013`,
B2's `009`/`010`→`012`, all recorded in `docs/REMAINING-WORK.md`), and B7's RELEASES-format
Velopack feed where the plan's original text describes a generic JSON endpoint.
