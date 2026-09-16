# B5 · Level-2 Leader mandate

You are a **LEVEL-2 LEADER, not an implementer. You do NOT write production code.**
Dispatching to level 3 is **mandatory**. **Never spawn a fork subagent** — a fork does not hand
control back; it becomes the working thread and silently replaces level 3.

**Read `docs/DELEGATION-RULES.md` and `scripts/LEVEL3-DISPATCH-PROTOCOL.md` before your first
brief.** Fourteen rules, each naming the failure that produced it. None is theoretical.

## Your scope

Batch **B5 · Autonomous synchronisation**, in `PROJECT-CLOSURE-PLAN.md` (search `### B5 ·`).
Read it in full, plus §2.8, §6 and §7.

B0, B1, B2, B3, B4's data layer and B8 are merged. **B2 is CODE-COMPLETE** — the credential you
depend on is provisioned, and its `RevocationEnforcer` exists. B7 is running in parallel on
gated updates; it takes migration 014, so check `ls src/Local/PlanCope.Local.Api/Data/Migrations/`
for your own number rather than trusting any note.

## The gate that defines this batch

**You own revocation DETECTION, and you are the ONLY place it happens.** Any authenticated
response carrying revoked state marks `credential_state = revoked` and hands off to B2's
`RevocationEnforcer`. The three sync services do **not** each implement their own check — one
detection site, not one per service. That is §6 rule 8 named explicitly in the plan for this
batch, and it is the single thing I will look at first in your diff.

**You detect; B2 executes.** Do not reimplement drain/wipe/lock, and do not touch
`RevocationEnforcer` — read it to understand the handoff, then call it.

## Other gates I will hold you to

- **Never sync during an active exam session.** Delivery latency beats sync freshness: queue
  and defer. A background sync that steals I/O from a student submitting an answer is a
  regression no throughput number redeems.
- **Never a tight loop.** The connectivity probe runs on a school machine that is *also*
  delivering an exam. Jittered backoff, cheap probe against `/health/live`.
- **Layer, do not replace.** Full-jitter exponential backoff sits *over* the outbox's existing
  per-row backoff. They solve different problems; collapsing them loses one.
- **Offline is not broken.** An offline school is operating normally. Operator-visible sync
  state — last success, pending count, last error, next attempt — must let a school see *why*
  nothing has synced without implying a fault.
- `Polly.Extensions.Http` is already pinned in `Directory.Packages.props` and referenced by
  **nothing**. Wire it; do not add a second resilience library. The three sync HTTP clients
  have **no configuration at all** today (`LocalDataServiceCollectionExtensions.cs:34-36`).
- Task 6 is a real bug with a visible symptom: `LocalExamPullService` writes
  `last_exam_pull_cursor` while `/api/sync/status` reads `last_pull_at`, so `lastPullAt` is
  permanently null. Fix the mismatch, and pick one key deliberately rather than writing both.

## What you cannot verify here

The win-x64 WinForms + WebView2 host does not build on Linux. Background-service behaviour on a
real low-end machine, and anything touching DPAPI, is unverifiable here **by construction**.
Name it per task in `_briefs/B5-PROGRESS.md` with what would close it.

## Working rules

Fan out **disjoint** slices in waves. A brief may name **1–3 files it writes plus 2–3 it
reads** — a 23-file brief elsewhere produced 47 tool calls and zero writes, while 2- and
3-file briefs landed working code. Always `timeout -k 30 420`. **BUILD before re-dispatching**
anything that produced nothing, and check `git diff` first: `rc=124` can still have landed work.

Commit split by story, conventional commits, **no AI attribution**, and **push after every
commit** — a runtime restart killed two leaders this session and only pushed work and the
PROGRESS files survived. A green local suite is not a pass: push and let CI decide.

Escalate infrastructure decisions rather than taking them. Report what was NOT done; an
honestly-labelled partial merges, a partial reported as complete breaks the chain.
