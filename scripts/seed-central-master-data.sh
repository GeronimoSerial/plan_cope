#!/usr/bin/env bash
# Seeds Central master data — provinces, departments, localities, schools — from
# asistencias.public.secciones into plan_cope, dry-run by default. It extracts five
# distinct-row CSVs from asistencias, splices them into
# scripts/seed-central-master-data.sql at the @@DATA markers, and feeds the result
# to ONE psql session against plan_cope that ends in ROLLBACK.
#
# DRY RUN BY DEFAULT, EXPLICIT COMMIT: with no flags this batch never commits —
# the SQL it loads ends with an explicit ROLLBACK as its last statement and
# success is reported as "DRY RUN — rolled back", never as an applied change.
# The ONLY way to commit is to pass --commit explicitly, which runs the identical
# spliced SQL body but pipes COMMIT; as its final statement instead of ROLLBACK;
# (the .sql file on disk is never modified). --commit demands confirmation first:
# either --yes was also passed, or an interactive prompt answered with the exact
# literal text COMMIT, and it refuses to run at all when stdin is not a terminal
# and --yes was not given. Before anything runs it prints the target host and
# database (never credentials). Success in that mode is reported as
# "COMMIT — applied".
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

# Flag parsing. --commit swaps only the trailing statement piped to psql
# (ROLLBACK; -> COMMIT;); --yes merely skips that mode's interactive
# confirmation. --yes without --commit is a harmless no-op: dry-run behavior
# does not change either way. Anything else is a usage error.
commit_mode=false
auto_yes=false
for arg in "$@"; do
  case "$arg" in
    --commit) commit_mode=true ;;
    --yes) auto_yes=true ;;
    *)
      echo "seed-central-master-data: unknown argument: $arg" >&2
      echo "usage: $0 [--commit] [--yes]" >&2
      exit 1
      ;;
  esac
done

# Print the target and confirm BEFORE any docker run, so a committing run always
# states what it is about to write to. PGHOST may be unset — then libpq's default
# applies inside the container — and PGUSER/PGPASSWORD are deliberately never
# echoed.
target_host="${PGHOST:-<unset — libpq default>}"
if [[ "$commit_mode" == true ]]; then
  echo "TARGET: host=${target_host} db=plan_cope"
  echo "*** COMMIT MODE: this will APPLY and PERSIST changes to plan_cope ***"
  if [[ "$auto_yes" != true ]]; then
    if [[ ! -t 0 ]]; then
      echo "seed-central-master-data: --commit requires --yes when stdin is not a terminal" >&2
      exit 1
    fi
    read -r -p "Type COMMIT to apply against ${target_host}/plan_cope: " confirmation
    if [[ "$confirmation" != "COMMIT" ]]; then
      echo "seed-central-master-data: confirmation not given — aborting" >&2
      exit 1
    fi
  fi
fi

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
# In commit mode the ONLY difference is the text piped to psql: the trailing
# ROLLBACK; line is swapped for COMMIT; in the stream. The .sql file on disk is
# never modified — its own ROLLBACK; is the sole line matching ^ROLLBACK;$ in the
# whole file, so the last-line substitution cannot touch anything else.
if [[ "$commit_mode" == true ]]; then
  final_stmt=(sed '$ s/^ROLLBACK;$/COMMIT;/')
else
  final_stmt=(cat)
fi

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
' scripts/seed-central-master-data.sql | "${final_stmt[@]}" | docker run --rm -i "${docker_env[@]}" postgres:17 psql -d plan_cope -v ON_ERROR_STOP=1 -f - >"$load_log" 2>&1
then
  cat "$load_log"
  echo "seed-central-master-data: load failed — transaction was not committed" >&2
  exit 1
fi

cat "$load_log"

# psql prints the ROLLBACK (or COMMIT) command tag; echo it only if absent so the
# line appears exactly once before the final status line.
if [[ "$commit_mode" == true ]]; then
  if ! grep -qx 'COMMIT' "$load_log"; then
    echo 'COMMIT'
  fi
  echo 'COMMIT — applied'
else
  if ! grep -qx 'ROLLBACK' "$load_log"; then
    echo 'ROLLBACK'
  fi
  echo 'DRY RUN — rolled back'
fi
