#!/usr/bin/env bash
# Fails when a Dockerfile's hand-maintained csproj COPY list has fallen behind the
# project's real reference graph.
#
# The .NET Docker images copy each .csproj individually before `dotnet restore`, to keep
# the restore layer cacheable. That list does not discover anything: add a project, wire
# it into an app, and the image build fails at restore with an error that names the
# missing project but not the Dockerfile that forgot it. This has now happened once
# (PlanCope.Shared.Grading), one cycle after the same shape of failure in the CI
# workflows. Two hand-maintained lists, same blind spot.
#
# A rule you must remember is weaker than a default you cannot express. This is the default.

set -uo pipefail
cd "$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)" || exit 1

# Walk ProjectReference transitively from a csproj, emitting absolute-ish repo paths.
refs_of() {
  local proj="$1" dir; dir=$(dirname "$proj")
  grep -oP '(?<=ProjectReference Include=")[^"]+' "$proj" 2>/dev/null | while read -r rel; do
    local resolved; resolved=$(realpath -m --relative-to=. "$dir/${rel//\\//}")
    echo "$resolved"
    refs_of "$resolved"
  done
}

mapfile -t dockerfiles < <(find src -name Dockerfile 2>/dev/null)
# A guard that examines nothing must FAIL, not report success. Exiting 0 on an empty
# sweep is how a check becomes decorative: it stays green while the thing it protects
# rots, and nobody notices because green is what they expect. This script found itself
# in exactly that state once — `find src` failed, the loop got no input, and it printed
# "all clear" over an unexamined tree.
if ((${#dockerfiles[@]} == 0)); then
  echo "check-dockerfile-project-list: found no Dockerfiles under src/ — refusing to report success." >&2
  echo "  Either the repo layout moved or this script is being run from the wrong directory." >&2
  exit 1
fi

status=0
for dockerfile in "${dockerfiles[@]}"; do
  # The app this image builds is the project living next to the Dockerfile.
  appdir=$(dirname "$dockerfile")
  app=$(find "$appdir" -maxdepth 1 -name '*.csproj' | head -1)
  [[ -n "$app" ]] || continue

  missing=()
  while IFS= read -r ref; do
    [[ -n "$ref" ]] || continue
    grep -qF -- "$(basename "$ref")" "$dockerfile" || missing+=("$ref")
  done < <(refs_of "$app" | sort -u)

  if ((${#missing[@]})); then
    status=1
    echo "$dockerfile does not COPY every project it restores:"
    printf '  missing: %s\n' "${missing[@]}"
    echo "  Add one COPY line per project, next to the others, BEFORE the bulk source copy."
  fi
done

((status == 0)) && echo "All ${#dockerfiles[@]} Dockerfiles COPY every project they restore."
exit $status
