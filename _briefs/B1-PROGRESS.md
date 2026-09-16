# B1 — Activation keys: progress log

Level-2 leader log. Every wave dispatched via `opencode run` per
`scripts/LEVEL3-DISPATCH-PROTOCOL.md`; nothing here was written directly by the leader.

## Decisions owed to the coordinator

### 1. Inherited B0 finding — `LoginResponse` missing access token deserialises to `null`

**Decision: B1 does not touch it. It stays with user authentication (`AuthController` /
`LoginResponse`), out of B1's boundary.**

Reasoning: the finding is specifically about `LoginResponse` (`PlanCope.Shared.Contracts.Auth`),
the *user* login/refresh contract. B1 never constructs or consumes a `LoginResponse` — it defines
its own, separate contracts (`ActivationRedeemResponse`, `ActivationRefreshResponse` in
`src/Shared/PlanCope.Shared.Contracts/Activation/ActivationContracts.cs`) for the *node*
credential flow. B1 does not inherit the bug by construction: every property on both of its
response types is declared `required`, so a response missing a field is a deserialization
exception, not a silent `null` a caller could forward as a real credential. This was a deliberate
design constraint given to both redeem/refresh implementers, specifically citing the
`ContractToleranceTests.LoginResponse_missing_access_token_becomes_null` finding as the failure
mode not to repeat.

What B1 does **not** do: it does not configure `UnmappedMemberHandling`/strict deserialization
globally, and it does not change `LoginResponse` or its pinning test. That fix, if made, belongs
to whichever batch owns user authentication — none of B1's remaining tasks touch that surface.
**Escalating to Opus**: someone needs to own tightening `LoginResponse` itself (or explicitly
accept the risk) — it is not naturally any batch's job on the current map, since it's an
already-shipped surface, not new work. Recording it here rather than letting it default to
"nobody's."

### 2. Rate-limiter storage and deployment topology

`ActivationRateLimitMiddleware` (committed in `f241db5`) uses `IMemoryCache` — per-process,
in-memory counters.

**Central is single-instance today.** Verified against `docs/ci-cd-velopack-coolify-plan.md` (the
only deployment plan in the repo): one Coolify app per service, images built once and deployed as
a single container; readiness/liveness are split for health-check purposes, not for load-balancing
across replicas, and nothing in `deploy/compose.dev.yml` / `deploy/compose.ci.yml` or the CI/CD
plan configures more than one Central API instance. Under this topology `IMemoryCache` gives
correct, un-diluted rate limiting.

**Forward risk, not fixed now, per explicit instruction not to add a distributed cache
unilaterally**: if Central is ever scaled horizontally behind a load balancer, this limiter's
effective ceiling multiplies by the instance count — exactly the scenario where the guard matters
most (a bearer-secret brute force spread across instances). If/when horizontal scaling is planned,
the counters need to move to a shared store (e.g. Postgres-backed counter table, since Central
already depends on Postgres and this avoids a new infra dependency; Redis is the alternative but
is a new dependency). That choice is the coordinator's/Opus's to make when it becomes relevant, not
B1's to pre-build speculatively (KISS — no infra nobody asked for yet).

## Wave 0 — schema + key issuance service (tasks 1–2, sequential, one implementer)

Dispatched alone per the plan ("tasks 1–2 first, one implementer"). First attempt (`w0`) timed out
at 420s doing pure exploration and never wrote a file — diagnosis: the brief made it re-discover
conventions (jsonb column typing, EF tooling availability) that were cheap for the leader to verify
up front. Re-dispatched (`w0b`) with those facts embedded directly in the brief; completed and
committed clean on the first attempt after that.

**Commit `5096c6e`** — `feat(central): add activation key schema and issuance service`.
- `sync.activation_keys`, `sync.node_credentials` tables; `sync.registered_nodes` extended with
  `fingerprint_hash`, `fingerprint_components` (jsonb), `cue`, `activation_key_id`, `enrolled_at`,
  `revoked_at`, `app_version`. Migration `20260915235041_AddActivationKeys`, generated via
  `dotnet ef migrations add`, not hand-authored.
- `ActivationKeyService`: Crockford base32 format `PCOPE-XXXXX-XXXXX-XXXXX-CC`, CRC-16/CCITT
  checksum (independently recomputable offline, no DB round trip needed to catch a typo),
  Argon2id hashing (`Konscious.Security.Cryptography.Argon2`, already a centrally-versioned
  package), constant-time verification. Pure predicates `IsExpired`/`IsRevoked`/`IsExhausted` kept
  separate so callers can report *which* check failed, not just pass/fail.
- Verified independently by the leader: `dotnet build PlanCope.slnx -warnaserror` green,
  `dotnet test tests/PlanCope.Central.Api.Tests` green (9 tests), grepped the diff for `ILogger`
  calls near key material — none found.

## Wave 1 — redeem/refresh + admin/rate-limiting (tasks 3–8, fanned out)

Re-scoped from the plan's literal "3–4 / 5–6 / 7–8, three implementers" grouping to **two**
disjoint slices, because tasks 7 and 8 do not have an independent file footprint — task 8
(revocation exposure) is one field on the refresh response owned by the redeem/refresh slice, and
the audit-trail half of task 7 has to live inside the same redemption code path. Forcing a third
implementer onto that would have meant two dispatches editing the same files. Recorded here per
the dispatch protocol: "shared-file pressure is a design signal" — this batch's real seam is
node-facing vs. admin-facing, not the plan's task numbering.

- **Slice R** — redeem/refresh, `NodeCredentialService`, revocation exposure on refresh, audit
  row on redemption. Files: `ActivationContracts.cs`, `TokenService`/`ITokenService` (extended,
  not forked), `NodeCredentialService.cs`, `ActivationController.cs`, two test files.
- **Slice M** — admin issue/list/revoke-key, list/revoke-node, rate-limiting middleware. Files:
  `ActivationAdminContracts.cs`, `ActivationAdminController.cs`,
  `ActivationRateLimitMiddleware.cs`, two test files.

Dispatched as a wave of 2 (`w1r`/`w1m`), both timed out at 420s. Diagnosed before re-dispatching,
per lesson 1:
- **Slice M** had written ~600 lines and was two trivial compile errors away from green (a
  mistyped `IActionResult`/`ActionResult<T>` return and one unguarded null). Re-dispatched as a
  narrow continuation (`w1m-cont`) naming the exact two errors and what was still missing (tests).
  That continuation itself hit the 420s wall again but got to `git add` before being killed; build
  was green, its own tests passed (10/10) when the leader ran them independently. **Accepted and
  committed by the leader as `f241db5`** — the implementer's work was correct, only the final
  `git commit` step didn't complete before the timeout.
- **Slice R** made much less progress: contracts + the `TokenService` extension, nothing else.
  Diagnosis from the transcript: the brief's requirement to "prove no DB hit happens" for the
  malformed-key case sent it down a rabbit hole building a throwaway EF `DbCommandInterceptor`
  console app to investigate the interception API — badly over-specified by the leader, not a
  problem with the implementer. Re-dispatched (`w1r-cont`) with the same scope; still timed out,
  same rabbit hole (visible in the transcript: building a second interceptor probe). **Fixed by
  the leader**: the requirement was rewritten to drop the automated DB-hit proof entirely in favour
  of a structural comment + an ordinary functional test, and every remaining ambiguity (AuditLog
  shape, `RegisteredNode.Status`/`NodeCode` conventions) was resolved in the brief instead of left
  for the implementer to discover. Third continuation (`w1r-cont2`) dispatched; **result pending**
  — status of this dispatch will be recorded here once it returns.

The two buildable-but-uncommitted files left over from Slice R's first two attempts
(`ITokenService.cs`/`TokenService.cs` extension, `ActivationContracts.cs`) were verified green by
the leader and committed as `6ea59f8` rather than left dirty across a wave boundary.

## DI wiring owed (deferred, not yet applied)

Neither slice touched `Program.cs` by design (both were told not to, to keep the wave disjoint —
`Program.cs` is a real collision point when two implementers edit it concurrently). Once Slice R
lands, a single narrow follow-up pass wires:
- `builder.Services.AddScoped<ActivationKeyService>();`
- `builder.Services.AddScoped<NodeCredentialService>();`
- `builder.Services.AddMemoryCache();`
- `app.UseMiddleware<ActivationRateLimitMiddleware>();` — before auth in the pipeline, so it
  short-circuits cheaply on the anonymous redeem route without touching the DB or the auth
  middleware first.

## Outstanding from the plan's task 7 ("compensating controls")

`max_activations` enforcement: done (Wave 0 + Slice R). Two-level revocation: done (Slice M).
Audit trail feeding anomaly detection: done as data (Slice R writes the `AuditLog` row); the
*alerting* half — "alert when one CUE enrols an unusual number of nodes in a window, or one key
enrols across implausibly many CUEs" — is explicitly **not built**. There is no existing
alerting/notification channel (no email/Slack/webhook integration anywhere in this codebase) to
deliver such an alert, and building one is an infrastructure decision outside a level-2 leader's
authority. Escalating: the audit data needed to detect these two patterns exists from this batch
onward; turning it into a delivered alert is a decision for Opus/coordinator on scope and channel.
