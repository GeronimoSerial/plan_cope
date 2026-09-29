#!/usr/bin/env bash
# Tests for scripts/next-release-version.sh.
#
# The script computes the patch bump of the highest stable SemVer tag
# (X.Y.Z only, prerelease and non-SemVer tags ignored) and falls back to
# 1.0.0 when no stable tag exists.
#
# Run locally:  bash scripts/tests/next-release-version.test.sh
# CI runs the same command from .github/workflows/ci.yml.

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
script="$repo_root/scripts/next-release-version.sh"

if [[ ! -f "$script" ]]; then
  echo "ERROR: script under test not found at $script" >&2
  exit 2
fi

cases=0
failures=0

assert_next() {
  local name="$1"
  local expected="$2"
  local tags="$3"
  local actual

  cases=$((cases + 1))
  if ! actual="$(printf '%s\n' "$tags" | bash "$script" --tags-from-stdin)"; then
    echo "FAIL: $name: script exited non-zero" >&2
    failures=$((failures + 1))
    return
  fi

  if [[ "$actual" == "$expected" ]]; then
    echo "PASS: $name -> $actual"
  else
    echo "FAIL: $name: expected '$expected', got '$actual'" >&2
    failures=$((failures + 1))
  fi
}

assert_next "single stable tag bumps patch" "1.0.3" "1.0.2"
assert_next "patch carry-over 1.0.9 -> 1.0.10" "1.0.10" "1.0.9"
assert_next "patch only, minor stays: 1.9.9 -> 1.9.10" "1.9.10" "1.9.9"
assert_next "major sorts numerically, not lexically" "10.0.1" $'9.0.0\n10.0.0'
assert_next "prerelease tags are ignored" "1.0.3" $'1.0.2\n2.0.0-rc.1\n1.0.2-rc.2'
assert_next "higher prerelease does not raise the baseline" "1.0.3" $'10.0.0-rc.1\n1.0.2'
assert_next "non-SemVer tags are ignored" "1.0.3" $'v1.5.0\nfoo\nrelease-2\n1.0.2\n1.0.2.3'
assert_next "leading zeros in major, minor, and patch are ignored" "1.0.3" $'1.0.2\n01.0.0\n1.00.0\n1.0.01\n1.0.09'
assert_next "empty tag list falls back to 1.0.0" "1.0.0" ""

# The env var path must behave like --tags-from-stdin.
cases=$((cases + 1))
env_actual="$(RELEASE_TAGS=$'1.2.3\n1.2.4' bash "$script")"
if [[ "$env_actual" == "1.2.5" ]]; then
  echo "PASS: RELEASE_TAGS env var is honored -> $env_actual"
else
  echo "FAIL: RELEASE_TAGS env var: expected '1.2.5', got '$env_actual'" >&2
  failures=$((failures + 1))
fi

echo ""
echo "$((cases - failures))/$cases assertions passed."

if [[ "$failures" -ne 0 ]]; then
  exit 1
fi
