#!/usr/bin/env bash
# Coordinator-side reaper for level-3 dispatches that never come back.
#
# A level-2 leader dispatches opencode in the FOREGROUND, so it is blocked for the
# whole call and cannot watch itself. Only the coordinator can. Two distinct failures
# look identical from the leader's seat and need different handling:
#
#   escaped   - older than its own `timeout`, so SIGTERM was ignored or never sent.
#               Nothing will ever stop it. Kill it.
#   not-writing - inside its window, burning CPU, writing nothing. AMBIGUOUS. Measured
#               outcomes split roughly evenly between a stuck loop and a model still
#               thinking that wrote everything in a late burst. Report it; never act on
#               it alone. Let the dispatch's own timeout decide unless it crosses the
#               hard ceiling.
#
# Exit 0 always: this is a probe, not a gate.

set -uo pipefail

DISPATCH_TIMEOUT=${DISPATCH_TIMEOUT:-420}   # must match the leader's `timeout` value
GRACE=${GRACE:-60}                          # SIGTERM delivery + shutdown slack
SAMPLE=${SAMPLE:-15}                        # seconds between the two io samples
YOUNG=${YOUNG:-150}                         # below this age, a dispatch is too young to judge
IDLE_TICKS=${IDLE_TICKS:-100}               # CPU delta below this over SAMPLE = waiting, not looping
CEILING=$((DISPATCH_TIMEOUT + GRACE))

# SCOPE: only ever consider dispatches whose cwd belongs to a worktree of THIS repo.
# `pgrep -x opencode` is machine-wide, and this script KILLS an ESCAPED process. On a
# machine running more than one project an unscoped kill reaches into somebody else's
# work — observed live, a dispatch in a sibling repo appeared in this script's output.
# A reaper that can kill outside its own blast radius is a worse hazard than the hang it
# was written to clear.
#
# Identity, not path names: every worktree of a repo shares one git common dir, so ask
# git rather than matching directory strings, which break under temporary worktrees.
OURS=$(git rev-parse --path-format=absolute --git-common-dir 2>/dev/null) || OURS=""

pids=$(for p in $(pgrep -x opencode || true); do
  cwd=$(readlink "/proc/$p/cwd" 2>/dev/null) || continue
  theirs=$(git -C "$cwd" rev-parse --path-format=absolute --git-common-dir 2>/dev/null) || continue
  [[ -n "$OURS" && "$theirs" == "$OURS" ]] && echo "$p"
done)
if [[ -z "$pids" ]]; then
  echo "no live level-3 dispatch"
  exit 0
fi

# First sample, so CPU and bytes-written can be compared against a later one.
declare -A t0_cpu t0_bytes
for pid in $pids; do
  t0_cpu[$pid]=$(awk '{print $14+$15}' "/proc/$pid/stat" 2>/dev/null || echo 0)
  t0_bytes[$pid]=$(awk '/^write_bytes:/{print $2}' "/proc/$pid/io" 2>/dev/null || echo 0)
done

sleep "$SAMPLE"

for pid in $pids; do
  [[ -d "/proc/$pid" ]] || { echo "pid=$pid finished during sampling — healthy"; continue; }

  age=$(ps -o etimes= -p "$pid" 2>/dev/null | tr -d ' ')
  cwd=$(readlink "/proc/$pid/cwd" 2>/dev/null)
  cpu=$(awk '{print $14+$15}' "/proc/$pid/stat" 2>/dev/null || echo 0)
  bytes=$(awk '/^write_bytes:/{print $2}' "/proc/$pid/io" 2>/dev/null || echo 0)

  d_cpu=$(( cpu - ${t0_cpu[$pid]:-0} ))
  d_bytes=$(( bytes - ${t0_bytes[$pid]:-0} ))

  if (( age > CEILING )); then
    # It outlived `timeout` + grace, so its own kill switch did not work.
    echo "ESCAPED pid=$pid age=${age}s > ceiling=${CEILING}s cwd=$cwd — killing"
    kill -9 "$pid" 2>/dev/null
    # opencode spawns children; a bare kill leaves them holding the tty.
    pkill -9 -P "$pid" 2>/dev/null
    echo "  killed. Tell the leader this slice died unfinished: re-dispatch it NARROWER,"
    echo "  and verify with git diff what the dead run left half-written."
  elif (( d_bytes == 0 && age < YOUNG )); then
    # Early in a dispatch the model is still streaming its first response; zero bytes
    # is the normal state, not a symptom. Judging here produces false positives that
    # send a leader chasing a slice that is working fine.
    echo "starting pid=$pid age=${age}s cpu+=${d_cpu} bytes+=0 cwd=$cwd — too young to judge (<${YOUNG}s)"
  elif (( d_bytes == 0 && d_cpu < IDLE_TICKS )); then
    # ~0.1s of CPU across a 15s window is a process blocked on the model API, not one
    # looping. A real reasoning loop pins a core.
    echo "waiting pid=$pid age=${age}s cpu+=${d_cpu} ticks bytes+=0 cwd=$cwd — near-idle, blocked on the API, not looping"
  elif (( d_cpu >= IDLE_TICKS && d_bytes == 0 )); then
    echo "NOT-WRITING pid=$pid age=${age}s cpu+=${d_cpu} ticks bytes+=0 cwd=$cwd"
    echo "  Burning CPU, writing nothing over ${SAMPLE}s. This is AMBIGUOUS, not a verdict:"
    echo "  measured outcomes are roughly half a stuck loop and half a model that was still"
    echo "  thinking and wrote everything in a late burst. Do not correct the leader on it."
    echo "  Its own timeout still has $(( CEILING - age ))s to run. Do not kill yet;"
    echo "  re-probe next cycle. Exit 0 from this family is not evidence of work."
  else
    echo "healthy pid=$pid age=${age}s cpu+=${d_cpu} bytes+=${d_bytes} cwd=$cwd"
  fi
done
