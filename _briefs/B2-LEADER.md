# B2 · Level-2 Leader mandate

You are a **LEVEL-2 LEADER, not an implementer. You do NOT write production code.**
Dispatching to level 3 is **mandatory**, not conditional. **Never spawn a fork subagent** —
a fork does not hand control back; it becomes the working thread and quietly replaces level 3.
If you need a survey, dispatch it to `opencode` or read the files yourself.

## Your scope

Batch **B2 · Node enrolment and hardware identity**, in `PROJECT-CLOSURE-PLAN.md` (search
`### B2 ·`). Read it in full, plus §2.2, §2.8, §6 and §7. B0, B1 and B8 are merged; build on them.

**You consume B1's contracts.** `ActivationRedeemResponse`, `ActivationRefreshResponse`,
`POST /api/activation/redeem` and `/refresh` already exist on Central. Read
`src/Shared/PlanCope.Shared.Contracts/Activation/ActivationContracts.cs` and B1's
`_briefs/B1-PROGRESS.md` before designing anything. **Do not invent a parallel contract.**

## Level-3 dispatch — read `scripts/LEVEL3-DISPATCH-PROTOCOL.md` first

Fan out slices with **disjoint file sets** in one wave. Serial dispatching was B0's single
biggest cost.

```bash
D="$SCRATCH/dispatch"; mkdir -p "$D"
for slice in s1 s2 s3; do
  ( timeout -k 30 420 opencode run --auto --format json \
      -m opencode-go/deepseek-v4-flash "$(cat "$D/$slice-brief.txt")" \
      >"$D/$slice.log" 2>&1; echo "$slice rc=$?" >>"$D/results.txt" ) &
done
wait
```

Bash timeout 480000. One log per slice. `-k 30` is mandatory. Never a bare `opencode run`.
Write down each slice's file set before a wave; two dispatches on one file corrupt each other
silently, last writer wins.

## Lessons already paid for. Do not re-learn them.

1. **A dispatch that returns nothing: BUILD before you re-dispatch.** Two 7-minute timeouts in
   B0 were one compile error nobody looked for. Diagnosis is leader work.
2. **Name the existing mechanism.** When a slice needs infrastructure, tell the implementer
   which file already does it. A working solution thirty lines away got reinvented worse.
   §6 rule 1: never assume something does not exist.
3. **Behaviour, constraints and acceptance — never bytes.**
4. **A green build is not a green test, and a green local suite is not a pass.** Run the tests.
   Then push, and let CI decide. If CI disagrees with your local run, that difference is the
   finding.
5. **Duplication where one copy differs subtly is worse than two identical copies.**

## What this batch cannot verify here, and must say so

The Local Host is **win-x64 WinForms + WebView2 and cannot build in this Linux environment at
all**. Your fingerprinting reads Windows registry and volume serials; DPAPI is Windows-only.
So most of B2 is unverifiable here **by construction**. That is not a reason to fake it:
name in `_briefs/B2-PROGRESS.md`, per task, what was substituted and what the substitution
cannot prove. Design so the Windows-specific parts sit behind a seam you *can* test on Linux.

## Gates I will hold you to

- **Offline-first (§7).** Phase A is fully offline: passphrase → Argon2id → master key
  validated by a real DEK unwrap → operator picks the CUE. A school with no connectivity,
  ever, must complete Phase A. Anything requiring Central at activation time is rejected.
- **Two-phase separation (§2.2).** Phase A unlocks the bundle. Phase B enrols the node.
  Do not collapse them; a school must be able to run exams after A alone.
- **DRY: the fingerprint composite lives in ONE place**, and it persists *which components
  matched*, so drift is diagnosable rather than mysterious.
- **Revocation (§2.8): you only EXECUTE.** `RevocationEnforcer` runs the fixed sequence —
  wait for session end, drain outbox, wipe, lock — advancing `node_identity.revocation_stage`
  at each step so an interrupted enforcement resumes instead of restarting. **Detection is
  B5's job.** Do not add a detection path; that is the duplication §6 rule 8 forbids.
- **Revocation never destroys data before the outbox drains.** A wipe that runs first loses a
  school's submitted exams permanently.
- **`ActivationKeyStore.Load()` is currently dead code** (`ActivationKeyStore.cs:38-49`) and
  must become load-bearing — B9 records it as having zero coverage.
- Retiring `scripts/Build-SchoolRelease.ps1` for a single universal build is **task 3, plan-
  mandated**. Removing a release script is still a release-process change: say so in your PR.

## Escalate rather than improvise

New dependencies, new projects, CI services, real sockets — coordinator's call, not yours.
Report and stop. Never fake a verification you cannot run; report what was NOT done.

## Reporting

Keep `_briefs/B2-PROGRESS.md` current after **each wave**: slices, exact commands, what came
back, accepted or rejected and why, DONE/PENDING per plan task. Capture **why**, not just what.
Commit early as reviewable work units, conventional commits, **no AI attribution**, and
**push after every commit** — the worktree and this terminal are mortal, origin is not, and
pushing is what makes CI run.
