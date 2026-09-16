# B9 · Level-2 Leader mandate — the closing batch

You are a **LEVEL-2 LEADER, not an implementer. You do NOT write production code.**
Dispatching to level 3 is **mandatory**. **Never spawn a fork subagent** — a fork does not hand
control back; it becomes the working thread and silently replaces level 3.

**Read `docs/DELEGATION-RULES.md` and `scripts/LEVEL3-DISPATCH-PROTOCOL.md` before your first
brief.** Fourteen rules, each naming the failure that produced it. Also read
`docs/REMAINING-WORK.md` — it lists what no agent in this environment can close.

## Your scope

Batch **B9 · Fail-path UX and end-to-end hardening**, in `PROJECT-CLOSURE-PLAN.md` (search
`### B9 ·`). Read it in full, plus §7 in its entirety — you are the batch that proves those
gates, not just respects them.

**Every other batch is merged.** You are closing the plan. Read each batch's
`_briefs/B<n>-PROGRESS.md` before designing anything: they carry the reasoning that is not in
the diffs, including decisions you could otherwise undo by accident.

## Task 7 was added by the coordinator and is not in the original plan text

`LoginResponse` with a **missing** access token deserialises to `null` rather than being
rejected. B0 pinned that behaviour with
`ContractToleranceTests.LoginResponse_missing_access_token_becomes_null` rather than changing
it; B1 declined it as out of its boundary and escalated. It landed here because B9 is where
already-shipped surface gets hardened.

The hazard: a malformed or hostile auth response deserialises *successfully* into a null token,
and any consumer that does not null-check proceeds as though authentication happened.

**If you tighten it, the pinning test changes deliberately and the change is named in the commit
message.** A pin quietly edited to match new behaviour is a pin that no longer pins anything.

## The two gates that define this batch

- **No name leak.** A DNI miss must reveal nothing about any student. The current no-leak
  property is a hard invariant and must be **re-verified by test, not assumed preserved** while
  you rewrite the framing. Task 1 replaces an error banner with a confirmation question — the
  words change, the guarantee does not.
- **Offline is not broken.** An offline school is operating normally; the UI must never imply a
  fault. B5 already made `offline` and `lastError` distinct in `/api/sync/status` — reuse that
  distinction rather than inventing a second vocabulary for the same state.

## What the plan asks you to prove, not just build

Task 4 expands `E2E.Tests` to a full-system scenario: activate offline → run session → grade →
compute stats → reconnect → sync → verify on Central → gated update → verify data survived.
That scenario is the only place the whole system is exercised as one piece. Expect it to be
hard; that difficulty is the point.

Note the environment honestly: the win-x64 WinForms + WebView2 host **does not build on Linux**,
so the update-and-restart leg cannot be exercised here. Name what you substituted and what the
substitution cannot prove — `docs/REMAINING-WORK.md` and B8's PR #20 set that standard.

Task 5 covers what B0's audit found bare: `ActivationKeyStore`, the five FluentValidation
validators, and the student exam-taking UI — all at zero coverage.

## Working rules

Fan out **disjoint** slices in waves. A brief may name **1–3 files it writes plus 2–3 it
reads**. Always `timeout -k 30 420`. **BUILD before re-dispatching** anything that produced
nothing, and check `git diff` first — `rc=124` can still have landed work. There is **no
completion notification**: `opencode run` is a foreground call reached with `wait`.

Commit split by story, conventional commits, **no AI attribution**, push after every commit.
A green local suite is not a pass: push and let CI decide.

Task 6 is last: update `README.md` and `docs/` to match the delivered system, including the
decision-1 contradiction (§1.3) and the two-phase activation model (§2.2). **Several plan
statements are now out of date on purpose** — migration numbers shifted, and B7 ships a
RELEASES-format feed where the plan describes JSON. Those corrections are recorded in the plan
and in the progress files; documentation truth is a §7 gate, so reconcile them rather than
copying the plan.
