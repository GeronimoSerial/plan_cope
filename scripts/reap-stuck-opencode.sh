#!/usr/bin/env bash
# Coordinator-side reaper for level-3 dispatches that never come back.
#
# A level-2 leader dispatches opencode in the FOREGROUND, so it is blocked for the
# whole call and cannot watch itself. Only the coordinator can. Two distinct failures
# look identical from the leader's seat and need different handling:
#
#   escaped   - older than its own `timeout`, so SIGTERM was ignored or never sent.
#               Nothing will ever stop it. Kill it.
#   spinning  - inside its window, burning CPU, writing nothing. This is the reasoning
#               loop. Report it; let the dispatch's own timeout do the killing unless
#               it crosses the hard ceiling.
#
# Exit 0 always: this is a probe, not a gate.

set -uo pipefail

DISPATCH_TIMEOUT=${DISPATCH_TIMEOUT:-420}   # must match the leader's `timeout` value
GRACE=${GRACE:-60}                          # SIGTERM delivery + shutdown slack
SAMPLE=${SAMPLE:-15}                        # seconds between the two io samples
CEILING=$((DISPATCH_TIMEOUT + GRACE))

pids=$(pgrep -x opencode || true)
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
  elif (( d_cpu > 0 && d_bytes == 0 )); then
    echo "SPINNING pid=$pid age=${age}s cpu+=${d_cpu} ticks bytes+=0 cwd=$cwd"
    echo "  Burning CPU, writing nothing over ${SAMPLE}s — the reasoning-loop signature."
    echo "  Its own timeout still has $(( CEILING - age ))s to run. Do not kill yet;"
    echo "  re-probe next cycle. Exit 0 from this family is not evidence of work."
  else
    echo "healthy pid=$pid age=${age}s cpu+=${d_cpu} bytes+=${d_bytes} cwd=$cwd"
  fi
done
