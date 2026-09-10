#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
packages_props="$repo_root/Directory.Packages.props"

if [[ ! -f "$packages_props" ]]; then
  echo "ERROR: Directory.Packages.props not found at $packages_props" >&2
  exit 2
fi

if ! command -v vpk >/dev/null 2>&1; then
  echo "ERROR: 'vpk' CLI not found on PATH. Install it first (e.g. dotnet tool install -g vpk) and re-run." >&2
  exit 2
fi

normalize() {
  local version="$1"
  version="${version#v}"
  version="${version#V}"
  printf '%s' "$version"
}

pinned_version="$(sed -n 's/.*<PackageVersion Include="Velopack" Version="\([^"]*\)"[[:space:]]*\/>.*/\1/p' "$packages_props")"

if [[ -z "$pinned_version" ]]; then
  echo "ERROR: Could not parse the Velopack PackageVersion from $packages_props." >&2
  exit 2
fi

# vpk has no --version flag: it answers "Unrecognized command or argument
# '--version'" and exits 1. The version is only printed in the help banner, as
# "Velopack CLI <x.y.z>, for distributing applications." Help exits non-zero on
# some vpk builds, so the status is tolerated and the parse is what validates.
vpk_output="$(vpk -h 2>&1 || true)"
vpk_version="$(printf '%s\n' "$vpk_output" \
  | sed -n 's/.*Velopack CLI \([0-9][0-9.]*\).*/\1/p' \
  | head -n 1)"

if [[ -z "$vpk_version" ]]; then
  echo "ERROR: Could not parse the vpk CLI version from 'vpk -h' output:" >&2
  printf '%s\n' "$vpk_output" >&2
  exit 2
fi

echo "Velopack package version (Directory.Packages.props): $pinned_version"
echo "vpk CLI version:                                    $vpk_version"

pinned_normalized="$(normalize "$pinned_version")"
vpk_normalized="$(normalize "$vpk_version")"

if [[ "$pinned_normalized" == "$vpk_normalized" ]]; then
  echo "OK: Versions match."
  exit 0
fi

echo "MISMATCH: Pinned Velopack version '$pinned_normalized' differs from installed vpk CLI version '$vpk_normalized'." >&2
exit 1
