#!/usr/bin/env bash
# scripts/reset-db.sh
#
# Wipes the local PostgreSQL container (volume included) and re-runs
# migrations + seeders. Use when:
#   - you want a clean dev environment
#   - the migration history is wedged (e.g. failed migration)
#   - you changed the DDL and want a fresh slate
#
# Safety: this script refuses to run unless both env vars confirm you
# mean it. The default `PG_RESET_CONFIRM` is "no" so an accidental
# tab-completion never nukes your data.
#
# Usage:
#   PG_RESET_CONFIRM=yes ./scripts/reset-db.sh
#
# It runs:
#   1. docker compose down -v          (drops the named volume `pgdata`)
#   2. docker compose up -d            (recreates PG with pgcrypto)
#   3. wait-for-ready loop (pg_isready)
#   4. pnpm db:migrate                 (apps/central)
#   5. pnpm db:seed                    (apps/central — geography, users, exams)
#   6. pnpm db:seed                    (apps/local — local_users, app_settings)

set -Eeuo pipefail

# ── Pre-flight ──
SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
REPO_ROOT="$(cd -- "${SCRIPT_DIR}/.." && pwd -P)"

log() { printf '[reset-db] %s\n' "$*" >&2; }
die() { printf '[reset-db] ERROR: %s\n' "$*" >&2; exit 1; }

# Confirmation gate. Default: abort.
if [[ "${PG_RESET_CONFIRM:-no}" != "yes" ]]; then
  die "Refusing to reset the database without explicit confirmation.
Set PG_RESET_CONFIRM=yes in the environment to proceed.

  PG_RESET_CONFIRM=yes $0"
fi

# Detect docker (compose v2 plugin or legacy v1 binary).
if command -v docker >/dev/null 2>&1; then
  if docker compose version >/dev/null 2>&1; then
    DC="docker compose"
  elif command -v docker-compose >/dev/null 2>&1; then
    DC="docker-compose"
  else
    die "docker found but 'docker compose' / 'docker-compose' is missing."
  fi
else
  die "docker not found on PATH. Install Docker Desktop or the docker CLI."
fi

# ── 1. Wipe the PG volume ──
log "stopping the postgres container and dropping the 'pgdata' volume…"
(cd "$REPO_ROOT" && $DC down -v)

# ── 2. Bring PG back up ──
log "starting a fresh postgres container…"
(cd "$REPO_ROOT" && $DC up -d postgres)

# ── 3. Wait for pg_isready ──
log "waiting for postgres to accept connections…"
ready=0
for _ in $(seq 1 30); do
  if (cd "$REPO_ROOT" && $DC exec -T postgres pg_isready -U plan_cope -d plan_cope >/dev/null 2>&1); then
    ready=1
    break
  fi
  sleep 1
done
if [[ "$ready" -ne 1 ]]; then
  die "postgres did not become ready within 30s. Check 'docker compose logs postgres'."
fi
log "postgres is ready."

# ── 4. Apply migrations (central) ──
log "running central migrations…"
pnpm --filter @plan-cope/central db:migrate

# ── 5. Seed central DB ──
log "seeding central database (geography, users, exams)…"
pnpm --filter @plan-cope/central db:seed

# ── 6. Seed local SQLite ──
log "seeding local database (local_users, app_settings)…"
pnpm --filter @plan-cope/local db:seed

log "done. Central PG is at postgres://plan_cope:plan_cope_dev@localhost:5432/plan_cope"
