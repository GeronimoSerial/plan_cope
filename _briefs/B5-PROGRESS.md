# B5 · Autonomous synchronisation — progress

## Status: CODE-COMPLETE for all 8 numbered tasks. Two acceptance criteria remain NOT
## VERIFIED for hardware reasons stated below — do not read either as done.

This leader wrote no production code. Every change was dispatched to a level-3
(`opencode`/`deepseek-v4-flash`) implementer, reviewed against its diff, built and tested before
being committed. Three waves, four dispatches.

## Decision: task 8 (revocation detection) needed almost no new code — it was already the
## single detection site B2 provisioned, and the risk was duplicating it, not building it

Before writing any brief, this leader read `RevocationEnforcer.cs`,
`RevocationEnforcementHostedService.cs`, `CentralCredentialHandler.cs` and
`NodeCredentialRefresher.cs` (all B2, merged, unmodified by this batch). The pipeline already
exists end to end: any 401 on any of the three sync `HttpClient`s (all wrapped by the same
`CentralCredentialHandler`) triggers `NodeCredentialRefresher.TryRefreshAsync`, which calls
Central's `/api/activation/refresh`; Central's response carries the one `NodeRevoked` signal
named explicitly for B5 in `ActivationController.cs:113-115`; `NodeCredentialRefresher` already
sets `credential_state = revoked` on that signal; `RevocationEnforcementHostedService` already
polls `RevocationEnforcer.TryAdvanceAsync` every 30s and drains-wipes-locks in that fixed order.
Grepped all three sync services for their own revocation checks — none exists. **One detection
site, already true before this batch started.** This batch's contribution to task 8 is that
`SyncBackgroundService` now calls the pull/push services automatically instead of only on
operator action, which is what actually closes the "detects it on the first successful call
after reconnect" criterion in practice — before this batch, that first call only happened if an
operator pressed something.

**Confirmed, not assumed:** re-read the three sync service files after landing
`SyncBackgroundService` — no revocation logic was added anywhere outside the existing
handler/refresher pair. `RevocationEnforcer.cs` was read and never touched, per the mandate.

## Decision: roster pull stays manual-only; the background service drives exam pull + outbox
## push only

The plan's task 1 says the background service "replaces manual invocation; the manual
endpoints stay for diagnostics." `pull-roster` needs a CUE and school year
(`LocalRosterPullService.PullAsync(cue, schoolYear, ...)`) that no sync_state key or node
identity record currently holds reliably — `NodeIdentity` has `Cue` but no school year. Rather
than inventing a new state key or guessing a default school year (KISS: no config knob nobody
asked for), this leader scoped the background service to exam pull + outbox push only, leaving
roster pull exactly as documented ("deliberately manual... no hosted service, timer, or implicit
pull on startup"). This was resolved before writing the `SyncBackgroundService` brief so the
implementer did not have to guess it.

## Decision: two separate backoff mechanisms, deliberately not merged

Per plan task 4 ("layer, do not replace"): `SyncBackgroundService`'s full-jitter exponential
backoff governs *when the next sync cycle runs* (only on connectivity-probe failure — capped at
5 × 2^attempt seconds up to 300s, jittered). `LocalOutboxPushService.RequeueAsync`'s existing
per-row backoff (unchanged, unreviewed by this batch beyond confirming it was not touched)
governs *which outbox rows are eligible for the next successful cycle*. They compose: the outer
schedule decides when to try again at all; the inner schedule decides which rows to attempt once
trying. Confirmed by reading `LocalOutboxPushService.cs` end to end before writing the
`SyncBackgroundService` brief — its `RequeueAsync` method was left completely alone.

## Per-task status (plan's own task numbering under "B5 · Autonomous synchronisation")

| # | Task | Status | Detail |
|---|---|---|---|
| 1 | `SyncBackgroundService` (`IHostedService`), manual endpoints stay | **DONE** | New file `src/Local/PlanCope.Local.Api/Services/SyncBackgroundService.cs`. Drives `LocalExamPullService.PullAsync` + `LocalOutboxPushService.PushAsync(200)` per cycle. `pull-roster` stays manual only, per the decision above. |
| 2 | Connectivity probe against `/health/live`, jittered backoff, never a tight loop | **DONE** | Unauthenticated `GET {central_url}/health/live` via a dedicated `HttpClient` (`nameof(SyncBackgroundService)`, 5s timeout, no `CentralCredentialHandler`, no Polly — a probe must stay a single cheap shot). On failure: full-jitter exponential backoff (base 5s, cap 300s, jitter via `Random.Shared`). On success: fixed 30s idle interval. |
| 3 | Wire `Polly.Extensions.Http` onto the three sync `HttpClient`s | **DONE** | New file `SyncResiliencePolicies.cs` + `PlanCope.Local.Api.csproj` package reference + `.AddSyncResilience()` chained onto the three existing `AddHttpClient` calls in `LocalDataServiceCollectionExtensions.cs`. Retry (3 attempts, exponential + jitter) plus a 30s circuit breaker after 5 consecutive transient failures. `NodeCredentialRefresher` and `EnrolmentEndpoints` clients deliberately left unresilient — out of this batch's scope. |
| 4 | Full-jitter backoff layered over the outbox's row-level backoff | **DONE** | See the decision record above. `LocalOutboxPushService.RequeueAsync` untouched. |
| 5 | Never sync during an active session | **DONE**, test-proven | `SyncBackgroundService` checks `ISessionRepository.GetActiveAsync` (status `active`/`paused`) before any network call, every tick, with no exception. `SyncBackgroundServiceTests.Never_calls_central_while_a_session_is_active` seeds an active session behind a handler that throws on any HTTP call and asserts zero calls. |
| 6 | Fix `last_pull_at` / `last_exam_pull_cursor` key mismatch | **DONE** | `LocalExamPullService.PullAsync` now writes both: `last_exam_pull_cursor` (unchanged, still the pagination cursor) and a new, separate `last_pull_at` timestamp (same JSON-serialized-`DateTimeOffset` pattern `LocalOutboxPushService` already used for `last_push_at`). `/api/sync/status`'s existing `last_pull_at` read now resolves. |
| 7 | Operator-visible sync state: last success, pending, last error, next attempt | **DONE** | `/api/sync/status` now also returns `lastError` (from `sync_last_error`) and `nextAttempt` (from `sync_next_attempt_at`), both written every `SyncBackgroundService` cycle. `healthy` changed from a hardcoded `true` to `string.IsNullOrEmpty(lastError)`. |
| 8 | Revocation detection, one site only | **DONE (pre-existing from B2, verified not duplicated)** | See the decision record above. No new detection code; `SyncBackgroundService`'s automatic calls are what actually makes the existing detection fire promptly instead of waiting for an operator. |

## NOT VERIFIED — named explicitly, per the leader mandate

- **Idle CPU attributable to sync is under 1% on the low-end target.** Cannot be measured here
  (Linux dev machine; the reference profile is a 2-core/4GB/HDD Windows machine, and
  `PlanCope.Local.Host` — win-x64 WinForms + WebView2 — does not build on Linux at all). What
  closes it: run the built `PlanCope.Local.Host` on the reference-profile machine
  (`docs/reference-profile.md`, from B8) for an extended idle period with no active session and
  sample CPU. The 30s idle interval and 5s probe timeout are the two numbers to tune if this
  measurement comes back over budget.
- **24-hour-offline soak with accumulated outbox, then reconnect, verified for zero
  duplicates.** Not run. Argued by construction instead: `SyncBackgroundService`'s probe-fail
  branch never calls pull/push, so an offline node accumulates outbox rows exactly as it did
  before this batch; on reconnect the probe succeeds and the very next cycle calls
  `LocalOutboxPushService.PushAsync`, whose duplicate-classification path
  (`itemResult?.Status is "accepted" or "duplicate"`) is pre-existing B1/B4 code, unmodified
  here, and already covered by `LocalOutboxPushTests.cs` (Local side) and
  `SyncPushPolicyTests.cs` (Central side, `PlanCope.Central.Api.Tests`). This is a structural
  argument, not a 24-hour measurement — closing it for real needs a script that pauses/resumes
  connectivity against a running `PlanCope.Local.Host` + `PlanCope.Central.Api` pair for a full
  day, which needs the reference-profile machine or a CI job long enough to run it.
- **A mid-push connection drop leaves the outbox consistent.** Not chaos-tested specifically for
  this batch. Argued by construction: `LocalOutboxPushService`'s existing catch block
  (`HttpRequestException`/`JsonException`/`InvalidOperationException`) already requeues every
  item in the batch with the row-level backoff on any mid-call failure, unmodified by this
  batch; `SyncBackgroundService` calls this same method and does not add its own retry-mid-call
  logic, so it inherits that guarantee rather than needing a new one. Not exercised with an
  actual killed connection.
- **`/api/sync/status`'s new `lastError`/`nextAttempt` fields have no endpoint-level test.**
  Consistent with the pre-existing pattern for this file — no `/api/sync/*` endpoint in this
  codebase has a dedicated endpoint test today (checked: no `SyncEndpointsTests.cs` exists for
  any of `/status`, `/pull-exams`, `/pull-roster`, `/push-outbox`). Not a regression introduced
  by this batch, but worth flagging if that changes.

## Test plan (this batch)

- [x] `dotnet build PlanCope.slnx -warnaserror` — 0 warnings, 0 errors (backend + host UI, run
      after every wave, most recently after the test-slice dispatch)
- [x] `dotnet test PlanCope.slnx` — full solution, all projects green: Local.Api.Tests 73/74
      (1 pre-existing unrelated skip), Central.Api.Tests 68/68, Shared.Tests 22/22,
      Shared.Grading.Tests 50/50, SyncCompat.Tests 46/46, RosterCrypto.Tests 7/7,
      Local.Host.Tests 7/7, E2E.Tests 1/1
- [x] `SyncBackgroundServiceTests.Never_calls_central_while_a_session_is_active` — session
      gate proven with a handler that throws on any call
- [x] `SyncBackgroundServiceTests.Probe_success_writes_operator_visible_sync_state` — probe
      wiring and `sync_state` write path proven
- [ ] 24h offline soak with reconnect + duplicate assertion (needs reference-profile time/hardware)
- [ ] Idle CPU <1% on low-end target (needs reference-profile hardware)
- [ ] Mid-push connection-drop chaos test (argued by construction, not exercised)

## Dispatch notes (level-3 waves, this session)

**Wave 1** (parallel, 3 dispatches, disjoint files): Polly wiring
(`PlanCope.Local.Api.csproj` + new `SyncResiliencePolicies.cs` + `LocalDataServiceCollection
Extensions.cs`), the `last_pull_at` fix (`LocalExamPullService.cs` only), and the
`/api/sync/status` `lastError`/`nextAttempt` fields (`SyncEndpoints.cs` only) — all disjoint
files, dispatched together per the protocol. Results: `rc=0`, `rc=0`, `rc=124`. The `rc=124`
slice (status fields) had already staged its complete, correct change before the clock killed
it — confirmed via `git diff` before re-dispatching anything, per rule 6 ("`rc=124` can still
have landed work"). No re-dispatch needed. Reviewed all three diffs independently, built once
over the combined result: green. The Polly slice deviated from the brief's suggested
two-`AddPolicyHandler`-calls shape (it wrapped retry + circuit-breaker into one custom
`DelegatingHandler` via `Policy.WrapAsync` instead) — functionally equivalent, reviewed and
accepted rather than re-dispatched, since the acceptance criteria (build green, no unrequested
abstraction, only the three named files touched) were all met.

**Wave 2** (single dispatch — `SyncBackgroundService` itself touches the same DI file Wave 1's
Polly slice touched, so it was serialized after Wave 1 landed, with that file's actual
post-Wave-1 content pasted into the brief verbatim rather than trusting a stale reading).
`rc=0`, landed clean on the first attempt, matched the brief's exact gate ordering
(session-check before any network call), backoff formula, and key-write contract.

**Wave 3** (single dispatch, test-only): `SyncBackgroundServiceTests.cs`, one new file, given
the exact constructor signatures and the repo's existing `RevocationEnforcerTests.cs` stub-http
pattern to copy rather than re-derive. `rc=0`. Reviewed the test file line by line before
trusting the green run — both assertions are real (call-count/exception-capture for the gate,
actual `sync_state` row reads for the write-path proof), not vacuous.

Every dispatch stayed within the 1-3-file budget. No dispatch needed a second attempt for being
too broad. All four commits pushed individually to `feat/b5-autonomous-sync` immediately after
each wave's review, per the durability rule.
