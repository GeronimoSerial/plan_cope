#!/usr/bin/env bash

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root" || exit 0

if [[ -f dotnet-tools.json || -f .config/dotnet-tools.json ]]; then
  dotnet tool restore
fi

npm ci

exit 0
