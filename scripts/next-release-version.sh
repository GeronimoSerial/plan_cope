#!/usr/bin/env bash
# Compute the next Plan Cope release version from existing git tags.
#
# Rule: take the highest stable SemVer tag (strict X.Y.Z, no prerelease, no
# leading zeros per strict SemVer) and bump PATCH by one. Prerelease tags
# (X.Y.Z-suffix) and any non-SemVer tag are ignored. When no stable tag exists
# the baseline is 1.0.0, so the first automatic release starts there.
#
# Input (in priority order):
#   1. --tags-from-stdin  read one tag per line from stdin (tests)
#   2. RELEASE_TAGS env var, newline-separated (tests / callers)
#   3. `git tag -l` in the current repository
#
# Output: the computed version on stdout, nothing else. Diagnostics go to stderr.
#
# Usage:
#   scripts/next-release-version.sh
#   printf '%s\n' 1.0.2 1.0.9 | scripts/next-release-version.sh --tags-from-stdin

set -euo pipefail

usage() {
  echo "Usage: $0 [--tags-from-stdin]" >&2
  echo "Computes the next release version (PATCH+1 of the highest stable SemVer tag, else 1.0.0)." >&2
}

tags_from_stdin=0
for arg in "$@"; do
  case "$arg" in
    --tags-from-stdin)
      tags_from_stdin=1
      ;;
    -h | --help)
      usage
      exit 0
      ;;
    *)
      echo "ERROR: unknown argument '$arg'." >&2
      usage
      exit 2
      ;;
  esac
done

if [[ "$tags_from_stdin" -eq 1 ]]; then
  tags="$(cat)"
elif [[ -n "${RELEASE_TAGS:-}" ]]; then
  tags="$RELEASE_TAGS"
else
  if ! command -v git >/dev/null 2>&1; then
    echo "ERROR: git is required to list tags (or pass --tags-from-stdin / RELEASE_TAGS)." >&2
    exit 2
  fi
  tags="$(git tag -l)"
fi

# Strict stable SemVer: X.Y.Z with no prerelease/build suffix. Appends the
# candidate to $best only when it is numerically greater, so sorting is decimal
# (10.0.0 beats 9.0.0), never lexical.
is_greater() {
  local candidate_major="$1" candidate_minor="$2" candidate_patch="$3"
  local best_major="$4" best_minor="$5" best_patch="$6"

  if ((10#$candidate_major != 10#$best_major)); then
    ((10#$candidate_major > 10#$best_major))
    return
  fi
  if ((10#$candidate_minor != 10#$best_minor)); then
    ((10#$candidate_minor > 10#$best_minor))
    return
  fi
  ((10#$candidate_patch > 10#$best_patch))
}

best=""
while IFS= read -r tag; do
  [[ -z "$tag" ]] && continue
  if [[ ! "$tag" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$ ]]; then
    continue
  fi

  major="${BASH_REMATCH[1]}"
  minor="${BASH_REMATCH[2]}"
  patch="${BASH_REMATCH[3]}"

  if [[ -z "$best" ]]; then
    best="$tag"
    continue
  fi

  IFS=. read -r best_major best_minor best_patch <<<"$best"
  if is_greater "$major" "$minor" "$patch" "$best_major" "$best_minor" "$best_patch"; then
    best="$tag"
  fi
done <<<"$tags"

if [[ -z "$best" ]]; then
  echo "1.0.0"
  exit 0
fi

IFS=. read -r best_major best_minor best_patch <<<"$best"
next_patch=$((10#$best_patch + 1))
echo "${best_major}.${best_minor}.${next_patch}"
