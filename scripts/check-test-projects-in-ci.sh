#!/usr/bin/env bash
# Fails when a test project exists on disk but no workflow ever runs it.
#
# The pipelines enumerate projects by hand (`for proj in ...`), they do not discover them.
# That fails OPEN: add a new test project and it silently never runs in CI, so its tests
# pass locally and guard nothing where merges are actually decided. This happened once
# already — a grading-engine test project landed in the solution and in no workflow.
#
# A rule you must remember is weaker than a default you cannot express. This is the default.

set -uo pipefail
cd "$(dirname "$0")/.."

missing=()
while IFS= read -r proj; do
  name=$(basename "$proj")
  grep -Rqs -- "$name" .github/workflows/ || missing+=("$proj")
done < <(find tests tools -name '*.Tests.csproj' | sort)

if ((${#missing[@]})); then
  echo "Test projects that no workflow runs:"
  printf '  %s\n' "${missing[@]}"
  echo
  echo "Add each to the Build and Test loops of the workflow that owns its area"
  echo "(.github/workflows/ci-central-api.yml or ci-local-app.yml), and check that"
  echo ".github/workflows/ci.yml has a path filter routing its source directory there —"
  echo "a job that never triggers is the same hole, one level up."
  exit 1
fi

echo "All $(find tests tools -name '*.Tests.csproj' | wc -l) test projects are referenced by a workflow."
