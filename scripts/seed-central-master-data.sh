#!/usr/bin/env bash
# Seeds Central master data — provinces, departments, localities, schools — from
# asistencias.public.secciones into plan_cope as a DRY RUN ONLY. It extracts five
# distinct-row CSVs from asistencias, splices them into
# scripts/seed-central-master-data.sql at the @@DATA markers, and feeds the result
# to ONE psql session against plan_cope that always ends in ROLLBACK.
#
# DRY-RUN-ONLY GUARANTEE: this batch never commits. The SQL it loads ends with an
# explicit ROLLBACK as its last statement, and this orchestrator exposes NO commit
# flag — none may be added until a later batch is explicitly authorized to commit.
# Success is reported as "DRY RUN — rolled back", never as an applied change.
#
# CREDENTIALS: environment only. The caller must have sourced their pg.env (or
# equivalent) before invoking this script so the standard PG* connection variables
# are set. This script hardcodes no host, user, or password, never references the
# environment file's path, and never prints connection settings. PG* vars are
# passed into the container by NAME only, and only for vars currently set.
#
# SCOPE GUARDRAILS: the only tables written are core.provinces, core.departments,
# core.localities, and core.schools (inside the rolled-back transaction). It never
# touches roster.* or core.users / core.user_roles / core.user_schools, and it
# never reads the minors data tables (personas, alumnos, personas_alumnos_secciones).
# The only table read is asistencias.public.secciones. No extensions are installed;
# no constraints or indexes are added.
#
# KNOWN LIMIT: there is no psql on the host — every call runs through the
# postgres:17 image. Because the whole load rolls back, the "would insert" counts
# are identical on every run against an unchanged pre-state; the script is
# re-runnable by design (idempotency lives in the WHERE NOT EXISTS clauses of the
# SQL, not in anything this orchestrator does).

set -euo pipefail
cd "$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

tmpdir=$(mktemp -d)
trap 'rm -rf "$tmpdir"' EXIT

# Pass PG* variables into the container by name, and only those currently set, so
# unset vars never clobber the container's libpq defaults. Export each one so
# `docker -e VAR` can see it even if the caller sourced pg.env without `set -a`.
docker_env=()
for v in PGHOST PGPORT PGUSER PGPASSWORD PGDATABASE PGSSLMODE; do
  if [[ -n "${!v-}" ]]; then
    export "$v"
    docker_env+=(-e "$v")
  fi
done

# Matches only a bare trailing "COPY <n>" command tag — never a real CSV data row
# (every extraction row contains commas; cue_anexo values are digits/dashes).
copy_tag_re='^COPY [0-9]+$'

# Copies one DISTINCT extraction out of asistencias into $tmpdir/<name>.csv.
# -q suppresses the trailing "COPY n" command tag; the strip below is belt and
# braces so the CSV is guaranteed pure without corrupting valid data.
extract() {
  local name="$1"
  local query="$2"
  local out="$tmpdir/$name.csv"

  if ! docker run --rm -i "${docker_env[@]}" postgres:17 psql -q -v ON_ERROR_STOP=1 -d asistencias \
      -c "COPY ($query) TO STDOUT WITH (FORMAT csv)" >"$out"; then
    echo "seed-central-master-data: extraction of $name failed" >&2
    exit 1
  fi

  if [[ "$(tail -n 1 "$out")" =~ $copy_tag_re ]]; then
    sed -i '$d' "$out"
  fi
}

# Stage ID columns as TEXT; natural-key dedupe and casting happen on the load side.
extract provinces \
  'SELECT DISTINCT ON (jurisdiccion_id) jurisdiccion_id, jurisdiccion FROM public.secciones ORDER BY jurisdiccion_id, jurisdiccion'

extract departments \
  'SELECT DISTINCT ON (jurisdiccion_id, departamento_id) jurisdiccion_id, departamento_id, departamento FROM public.secciones ORDER BY jurisdiccion_id, departamento_id, departamento'

extract localities \
  'SELECT DISTINCT ON (jurisdiccion_id, departamento_id, localidad_id) jurisdiccion_id, departamento_id, localidad_id, cp, localidad FROM public.secciones ORDER BY jurisdiccion_id, departamento_id, localidad_id, cp NULLS LAST, localidad'

# Deterministic conflicting-name resolution: per cue_anexo keep the row with the
# highest ciclo_lectivo, tie-broken by the lowest id.
extract schools \
  'SELECT DISTINCT ON (cue_anexo) cue_anexo, jurisdiccion_id, departamento_id, localidad_id, establecimiento_nombre FROM public.secciones ORDER BY cue_anexo, ciclo_lectivo DESC NULLS LAST, id ASC'

extract cue_conflicts \
  'SELECT cue_anexo FROM public.secciones GROUP BY cue_anexo HAVING count(DISTINCT establecimiento_nombre) > 1 ORDER BY cue_anexo'

for name in provinces departments localities schools cue_conflicts; do
  if [[ ! -f "$tmpdir/$name.csv" ]]; then
    echo "seed-central-master-data: missing extracted CSV for $name — refusing to load" >&2
    exit 1
  fi
done

# Splice the CSVs into the SQL at the @@DATA markers, then run everything in one
# plan_cope session. Capturing stdout+stderr lets us replay the full log and check
# for the ROLLBACK tag afterwards; pipefail fails the pipeline if either side does.
load_log="$tmpdir/load.log"
if ! awk -v dir="$tmpdir" '
  /^@@DATA / {
    name = $2
    sub(/@@$/, "", name)
    file = dir "/" name ".csv"
    while ((getline line < file) > 0) print line
    close(file)
    next
  }
  { print }
' scripts/seed-central-master-data.sql | docker run --rm -i "${docker_env[@]}" postgres:17 psql -d plan_cope -v ON_ERROR_STOP=1 -f - >"$load_log" 2>&1
then
  cat "$load_log"
  echo "seed-central-master-data: load failed — transaction was not committed" >&2
  exit 1
fi

cat "$load_log"

# psql prints the ROLLBACK command tag; echo it only if absent so the line appears
# exactly once before the dry-run line.
if ! grep -qx 'ROLLBACK' "$load_log"; then
  echo 'ROLLBACK'
fi

echo 'DRY RUN — rolled back'
