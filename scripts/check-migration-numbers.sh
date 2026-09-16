#!/usr/bin/env bash
# Fails when two Local migrations claim the same sequence number.
#
# DbUp applies embedded scripts in FILENAME order, so a duplicate number does not error —
# it resolves alphabetically and silently destroys the one property the numbering exists
# for: that the number is a unique position in the sequence. The next person adding a
# migration cannot tell which of the two came first, or why there are two.
#
# This is a hand-maintained global sequence edited by parallel branches, so collisions are
# structural, not careless: each branch sees only the migrations that existed when it
# branched. It happened three times in one afternoon (B8/B3, then B2). A branch cannot
# detect this; only a check against the merged tree can.

set -uo pipefail
cd "$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)" || exit 1

DIR=src/Local/PlanCope.Local.Api/Data/Migrations

mapfile -t files < <(find "$DIR" -name '*.sql' -printf '%f\n' 2>/dev/null | sort)
# An empty sweep is a broken check, not a passing one.
if ((${#files[@]} == 0)); then
  echo "check-migration-numbers: found no migrations under $DIR — refusing to report success." >&2
  exit 1
fi

dupes=$(printf '%s\n' "${files[@]}" | grep -oE '^[0-9]+' | sort | uniq -d)

if [[ -n "$dupes" ]]; then
  echo "Duplicate Local migration numbers:"
  while read -r n; do
    echo "  $n:"
    printf '    %s\n' "${files[@]}" | grep -E "    ${n}_"
  done <<<"$dupes"
  echo
  echo "Rename the later one to the next free number. Do not edit a migration that has"
  echo "already been applied anywhere, and do not merge two migrations into one to avoid"
  echo "the rename — keeping them separate keeps each schema change independently revertible."
  exit 1
fi

echo "All ${#files[@]} Local migrations have distinct sequence numbers."
