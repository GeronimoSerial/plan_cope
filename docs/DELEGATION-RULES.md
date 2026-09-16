# Delegation rules

Rules established while running `PROJECT-CLOSURE-PLAN.md` through a three-tier topology —
Opus coordinator, Sonnet batch leaders, `deepseek-v4-flash` implementers. Every rule here was
paid for by a failure in this session, and each one names the failure so nobody removes it on
the grounds that it looks obvious.

---

## 1. Verify by evidence, never by an agent's summary

`worker_done` is a claim. A green build is a claim. A leader saying "all tests pass" is a claim.

Read the diff. Run the suite. Check `/proc`. In this session every one of the following was
first reported as fine and turned out otherwise: a migration that broke offline-first, a test
project that ran nowhere, a Dockerfile missing a project, a contract pin that needed checking,
and a guard of my own that reported success over a tree it never examined.

## 2. The easy signal lies, and the two errors do not cost the same

Six times a cheap check gave the wrong answer here: zero bytes read as a stalled dispatch,
a screen with no spinner read as a deadlocked leader, a `skipping` CI bucket read as a
failure, a grep for `fork` that matched the mandate forbidding forks, `terminal wait
--for tui-idle` satisfied by a *shell* prompt before the agent existed, and a `find` that
failed silently.

Measure the thing that matters — CPU deltas from `/proc/<pid>/stat`, bytes from
`/proc/<pid>/io`, the rendered screen rather than the output stream.

**And weigh the two mistakes differently.** Believing a stuck agent is working costs one
review cycle. Believing a working agent is stuck costs its output, because the correction is
"that slice is dead, re-dispatch" — destructive. When uncertain, wait and re-probe.

## 3. Level 2 leads; it does not implement, and it does not fork

A leader that writes the production code is author and reviewer at once, which is the hole
this topology exists to close.

**Leaders must never spawn a fork subagent.** A fork does not hand control back — it becomes
the working thread and silently replaces level 3. One did here, reaching 197k tokens while
committing production code with the reviewing thread idle. Detect it by the terminal's thread
indicator: `● fork` alongside `◯ main`. A missing `_briefs/B<n>-PROGRESS.md` while siblings
have theirs is the cheaper second signal — no dispatch log means no record of what was
delegated versus self-written, and it cannot be reconstructed afterwards.

## 4. A brief costs its FILE COUNT, not its word count

Measured: a 14,944-byte brief naming **23 files** produced 47 tool calls — 29 reads, 16 bash,
2 glob — and **zero writes** before the 420s clock killed it. In the same batch, briefs of
2,112 and 3,304 bytes naming 2 and 3 files both landed working code. Everything above ~3.5 KB
and 5 files produced nothing.

Target per dispatch: **1–3 files it may write, plus at most 2–3 it must read.** More is a plan
wearing a brief's clothes. The tell is `grep -c '"tool":"read"'` far exceeding the number of
files the slice should produce.

**Resolve the questions before writing the brief.** The leader already read those files to
write it at all. Handing over a resolved fact instead of a reading list saved a full
round-trip more than once.

## 5. Fan out disjoint slices; serial dispatching is the biggest cost

Three concurrent dispatches complete in **67s wall** against ~200s serial, with no contention
on the shared opencode database. Dispatch serially only when a slice genuinely reads what a
sibling writes.

**Disjointness is the safety rule.** Two dispatches editing one file corrupt each other
silently — last writer wins, the loser vanishes, no error anywhere. Write down each slice's
file set before a wave. If most slices collide, the batch is not decomposed along its real
seams yet.

Reviewing a wave does not get cheaper: review each diff independently *before* running the
suite once over the combined result.

## 6. Diagnose before re-dispatching

Two 7-minute timeouts in B0 were **one compile error** nobody built for. Diagnosis is leader
work, not implementer work.

A dispatch that produced nothing has five distinct causes needing different responses:

| Exit | Signature | Cause | Response |
|---|---|---|---|
| 0 | banner, no tool calls | `v4.1-flash` does not call tools | use `deepseek-v4-flash` |
| never returns | CPU, no output | `--auto` missing | add `--auto` |
| 0 | `"reason":"length"` in the JSON | prose until the 32k output cap | forbid narration, demand files |
| 124 | many reads, zero writes | brief named too many files | split by file set |
| 124 | few tool calls | genuinely out of clock | **check `git diff` — work may have landed** |

`rc=0` is the more dangerous result: a timeout is loud, exit 0 looks like success in every
log line a leader reads.

## 7. A green local suite is not a pass

Local runs prove the code works against the substitutes you chose — InMemory providers,
TestServer handlers, temp SQLite, your CPU, your locale. Push and let CI decide.
**When CI disagrees with a local run, that difference IS the finding.**

Live example: a test passing on Linux and failing on `windows-latest` with a Dapper
materialization error, invisible to every local run.

## 8. Name what you could not verify, above what you achieved

A reviewer must meet the limits before the accomplishment. The best artefact this session
opened with *"CODE-COMPLETE, NOT VERIFIED — do not read as done"*, then marked each acceptance
criterion PROVEN or NOT PROVEN with the hardware needed to close it.

Say which machine a benchmark ran on. A performance number from a 16-core dev box has not
measured a 2-core machine with a 5400rpm disk — it has measured a different computer.

## 9. Hand-maintained lists go stale; close it with a guard, not a reminder

Three failures shared one shape: **a new project or migration is invisible to a list nobody
updates.** CI workflows enumerate test projects. Dockerfiles enumerate `.csproj` files for the
restore layer. Migrations are a global sequence edited by parallel branches — it collided
**four times**.

**A default you cannot express beats a rule you must remember.** Three guards now run in the
always-on CI job. Same principle inside the code: an enum whose zero value is a *valid* policy
lets `default(T)` grade an exam nobody assigned a rule to; make the default unrepresentable
and the existing throw catches it everywhere, forever.

## 10. A guard must fail loudly, must be proven to fail, and must state what it misses

The Dockerfile guard originally exited 0 when its `find` failed from the wrong directory —
**reporting success over a tree it never examined.** That is how a check becomes decorative:
green while the thing it protects rots, and nobody looks because green is expected.

So: refuse to report success on an empty sweep. **Validate a new guard by making it fail
against the real defect** — one that never fires proves nothing. And write down what it does
*not* cover: the migration-number guard checks one tree, so two open PRs can each look
consistent and still collide when the second merges. A guard that hides its blind spot
manufactures the false confidence it was written to prevent.

## 11. Only the coordinator sees across batches

Leaders cannot see each other's unmerged work. Migration numbers, shared model files and
published contracts collide between worktrees that are each internally correct. When it
happens, say plainly that it is nobody's mistake — it is the topology — and resolve it on the
batch that is last in the merge order.

## 12. Durability: commit and push, because the reasoning is what dies

An Orca terminal does not survive a session restart. Files on disk live; the agent's context
does not. Three separate batches accumulated 11–18 uncommitted files.

What is lost is never the code — it is **why**. Why a field is nullable, why a policy went
into the checksum rather than beside it, which brief shapes produced usable output. None of it
is reconstructible from a diff. Commit split by story, push after every commit, and keep the
PROGRESS file answering *why*.

## 13. Correct at the right altitude, and be willing to be wrong

Do not correct a leader for reading before dispatching, for taking longer to be careful, or
for a design that differs from yours but meets the guarantee. A leader here declined a
recommendation of mine and was right to; another found two hazards in my own finding that I
had missed. **A leader that executes everything the coordinator says is not reviewing
anything**, and the review step is the whole point.

When you are wrong, say so plainly and move on. I was wrong about an unnormalised write path,
about a renumbering that needed no waiting, and about my own guard being broken when it was
reporting correctly.

## 14. Scaling down is the owner's call

Infrastructure decisions — new dependencies, new projects, CI services, real sockets — belong
to the coordinator, not a leader. Decisions that change what ships, or that cost money or
access, belong to the owner. Report and stop rather than deciding, and surface rather than
act: a stale pull request someone else opened is theirs to close.
