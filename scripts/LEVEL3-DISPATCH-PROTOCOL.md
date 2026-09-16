# Level-3 dispatch protocol

Read this before writing any slice brief. It replaces one-slice-at-a-time dispatching.

## The rule

**Fan out every slice whose file set is disjoint from its siblings, in a single Bash call.**
Dispatch serially only when a slice genuinely reads what a sibling writes.

Measured on this machine 2026-09-15: three concurrent dispatches completed in **67 s wall**,
all exit 0, all wrote their files, no SQLite contention on the shared `opencode.db` despite it
being 6.6 GB in WAL mode. Serially the same three cost ~200 s. The saving compounds: B0 issued
four sequential dispatches, of which the Central migration, the Local schema and the two test
projects touch entirely disjoint trees. They could have run as one wave.

## The invocation

```bash
D="$SCRATCH/dispatch"; mkdir -p "$D"
for slice in a1 a2 b4; do
  ( timeout -k 30 420 opencode run --auto --format json \
      -m opencode-go/deepseek-v4-flash "$(cat "$D/$slice-brief.txt")" \
      >"$D/$slice.log" 2>&1; echo "$slice rc=$?" >>"$D/results.txt" ) &
done
wait
cat "$D/results.txt"
```

Bash timeout 480000 ms. One log file per slice — a merged stream cannot be attributed when
one of three fails, and you will be reviewing all three diffs at once.

## Disjointness is the safety rule, and it is on you

Two dispatches editing the same file corrupt each other silently: last writer wins, and the
loser's work vanishes with no error anywhere. Before a wave, write down the file set each slice
may touch and confirm no path appears twice. A slice whose brief says "and update any call
sites" has an unbounded file set — it cannot go in a wave. Narrow it or run it alone.

Shared-file pressure is a design signal. If most slices in a batch collide, the batch is not
decomposed along its real seams yet.

## What does not change

- `timeout -k 30 420` on every dispatch, no exceptions.
- Exit 0 is not evidence of work. Confirm each slice with `git diff` over *its own* files.
- Review every returned diff adversarially. A wave means three diffs to review, not three
  diffs to trust.
- Briefs specify behaviour, constraints and acceptance — never bytes.

## Reviewing a wave

Review the diffs **independently**, one slice at a time, before running the suite once over
the combined result. A green suite over a merged wave tells you the wave works together; it
tells you nothing about whether any individual slice did what its brief asked. When a wave's
combined suite fails, bisect by slice file set rather than re-dispatching the whole wave.

## Judging a live dispatch — what zero bytes actually means

`scripts/reap-stuck-opencode.sh` classifies each live dispatch by sampling `/proc/<pid>/stat`
and `/proc/<pid>/io` twice. Zero bytes written is **not** by itself a symptom, and reading it as
one produces false positives that send a leader chasing a slice that is working fine.

| Verdict | Signature | Meaning |
|---|---|---|
| `starting` | age < 150 s, no bytes | Still streaming its first response. Normal. |
| `waiting` | CPU delta < 100 ticks over 15 s, no bytes | Blocked on the model API. A real loop pins a core; ~0.1 s of CPU across 15 s does not. |
| `healthy` | CPU and bytes both rising | Working. |
| `SPINNING` | CPU rising **hard**, bytes flat, past the young window | The reasoning-loop signature. Do not kill — let its own timeout run; arm a `Monitor` on the pid. |
| `ESCAPED` | age > timeout + grace | Its kill switch failed. Kill it and its children. |

This was learned the hard way: the first version classified any zero-byte process as SPINNING and
flagged a dispatch at 86 s that was simply waiting on the API. Fifty-five seconds later the same
pid had written 2.3 MB. **A classifier that cries wolf is worse than no classifier**, because the
correction it triggers — "that slice is dead, re-dispatch narrower" — throws away working output.

Two limits worth stating plainly. `write_bytes` counts *all* process I/O, including opencode's
own session database, so rising bytes proves the process is alive and doing work, never that it
wrote to the worktree — `git status` is the only proof of that. And a dispatch can start and die
between two review cycles, so a clean reading never proves nothing was reaped.
