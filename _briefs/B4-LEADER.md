# B4 · Level-2 Leader mandate

You are a **LEVEL-2 LEADER, not an implementer. You do NOT write production code.**
Dispatching to level 3 is **mandatory**, not conditional. **Never spawn a fork subagent** —
a fork does not hand control back; it becomes the working thread and silently replaces
level 3. For a survey, dispatch to `opencode` or read the files yourself.

## Your scope

Batch **B4 · Local statistics and offline rollups**, in `PROJECT-CLOSURE-PLAN.md` (search
`### B4 ·`). Read it in full, plus §6, §7 and the small-cohort privacy resolution around
line 805. B0, B1, B3 and B8 are merged; you are branched from `e7d1d59` which has all of them.

**You read B3's result tables.** `PlanCope.Shared.Grading`, `011_Grading.sql` and the
`CentralAttemptResult` model are merged. Read them before designing anything — do not
recompute grades, do not reimplement a grading formula. §6 rule 2: no grading logic outside
`PlanCope.Shared.Grading`, and that includes anything that looks like re-deriving a score
from answers while "just aggregating".

## Two plan corrections you must apply

1. **Your migration is `013_Stats.sql`, not `011_Stats.sql`.** The plan names 011, but 009
   went to B8, 010 and 011 to B3, and B2's open PR takes 012. Verify with
   `ls src/Local/PlanCope.Local.Api/Data/Migrations/` after any rebase rather than trusting
   this note — the sequence is hand-maintained and has collided four times today.
2. **One acceptance criterion cannot be met here and must not be faked.** *"A rollup query
   returns in under 200 ms on the low-end target profile"* needs a 2-core/4 GB/HDD Windows
   machine, which does not exist in this environment — and the win-x64 host cannot even
   build on Linux. Measure what you can, state the hardware you measured on, and report the
   criterion as NOT VERIFIED with the hardware needed. See B8's PR #20 description for the
   standard I hold this to.

## Level-3 dispatch — read `scripts/LEVEL3-DISPATCH-PROTOCOL.md` first

Fan out slices with **disjoint file sets** in one wave; serial dispatching was B0's biggest
cost. `timeout -k 30 420 opencode run --auto --format json -m opencode-go/deepseek-v4-flash`,
Bash timeout 480000, one log per slice, never a bare `opencode run`. Write down each slice's
file set before a wave — two dispatches on one file corrupt each other silently.

The protocol's four-row failure table is not optional reading: **`rc=0` with
`"reason":"length"` means the model wrote prose until the 32 k output cap and produced
nothing**, and **`rc=124` can still have landed work** — check `git diff` before discarding
a timed-out slice.

## Gates I will hold you to

- **Offline (§7).** Every statistic computes and displays with the network cable unplugged.
  This is the whole point of the batch, not a nice-to-have.
- **DRY: ONE cohort suppression filter.** Minimum cohort is 5, applied **only** to provincial
  and regional aggregates; a school looking at its own students suppresses nothing, because
  it already knows them. Implement this as **one filter parameterised by the existing
  `roster_scope` claim — not two aggregation paths.** Two paths is the duplication §6 rule 8
  forbids, and it is the kind where one copy drifts and starts leaking small cohorts.
  Suppressed cells report "cohorte insuficiente", never a zero or a blank.
- **Incremental, not full-scan.** Recompute only the affected
  `(cue, school_year, course, exam_version)` tuple. The plan calls this a low-end-hardware
  constraint, not an optimisation — a full table scan on a 5400 rpm disk is a hung console.
- **`blank` and `incorrect` are distinct figures everywhere.** Collapsing them destroys the
  only signal that tells a teacher whether students did not know or did not reach the question.
- **Rollups must match a full rebuild exactly**, proven by a reconciliation test, not asserted.

## Escalate rather than improvise

New dependencies, new projects, CI services — coordinator's call. Report and stop. Never fake
a verification you cannot run; report what was NOT done.

## Reporting

Keep `_briefs/B4-PROGRESS.md` current after **each wave**: slices, exact commands, what came
back, accepted or rejected and why, DONE/PENDING per plan task, and **why** — not just what.
Commit early as reviewable work units, conventional commits, **no AI attribution**, and push
after every commit. A green local suite is not a pass: push and let CI decide.
