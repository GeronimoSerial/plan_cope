#!/usr/bin/env bash
set -euo pipefail

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
cd "$repo_root"
for command in docker dotnet; do
  if ! command -v "$command" >/dev/null 2>&1; then
    echo "Required command not found on PATH: $command" >&2
    exit 1
  fi
done

dotnet tool restore

container="plancope-mig-test-$$"
port=${MIGRATION_TEST_PORT:-55433}
connection="Host=localhost;Port=$port;Database=postgres;Username=postgres;Password=test"
previous=20260929015416_AddExamVersionSourceVersion
migration=20260929120000_EnforceOnePackagePerExamVersion
divergent_log=$(mktemp)

cleanup() {
  docker rm -f "$container" >/dev/null 2>&1 || true
  rm -f "$divergent_log"
}
trap cleanup EXIT
docker run -d --rm --name "$container" -e POSTGRES_PASSWORD=test -p "$port:5432" postgres:17-alpine >/dev/null
for _ in $(seq 1 60); do
  docker exec "$container" pg_isready -U postgres >/dev/null 2>&1 && break
  sleep 1
done
export PLANCOPE_CENTRAL_DB="$connection"
dotnet ef database update "$previous" --project src/Central/PlanCope.Central.Migrations --startup-project src/Central/PlanCope.Central.Migrations

psql_in() { local database=$1; shift; docker exec -i "$container" psql -v ON_ERROR_STOP=1 -U postgres -d "$database" "$@"; }
psql_in postgres <<'SQL'
INSERT INTO exam.exams ("Id","Code","Title","Status","CreatedAt","UpdatedAt") VALUES ('ex-good','good','Good','Published',now(),now());
INSERT INTO exam.versions ("Id","ExamId","VersionNumber","SchemaVersion","Status","CreatedAt","UpdatedAt") VALUES ('ver-good','ex-good',1,1,'Published',now(),now());
INSERT INTO publication.packages ("Id","ExamVersionId","PackageVersion","Checksum","Manifest","Status","CreatedAt","PublishedAt") VALUES
('pkg-good-keep','ver-good',1,'same','{}','Published',now()-interval '2 days',now()-interval '2 days'),
('pkg-good-drop','ver-good',2,'same','{}','Published',now()-interval '1 day',now()-interval '1 day');
INSERT INTO publication.targets ("Id","PublicationPackageId","TargetType","TargetId","CreatedAt","UpdatedAt") VALUES
('target-a','pkg-good-keep','school','school-a',now(),now()),('target-b','pkg-good-drop','school','school-b',now(),now());
INSERT INTO sync.cursors ("Id","NodeId","CursorKey","CursorValue","UpdatedAt") VALUES
('cur-a','node-a','package:pkg-good-drop','value-a',now()),
('cur-b1','node-b','package:pkg-good-drop','value-b-drop',now()),
('cur-b2','node-b','package:pkg-good-keep','value-b-keep',now());
SQL
dotnet ef database update "$migration" --project src/Central/PlanCope.Central.Migrations --startup-project src/Central/PlanCope.Central.Migrations
psql_in postgres <<'SQL'
DO $$ BEGIN
 IF (SELECT count(*) FROM publication.packages WHERE "ExamVersionId"='ver-good') <> 1 THEN RAISE EXCEPTION 'expected one package'; END IF;
 IF NOT EXISTS (SELECT 1 FROM publication.packages WHERE "Id"='pkg-good-keep') THEN RAISE EXCEPTION 'wrong keeper'; END IF;
 IF (SELECT count(*) FROM publication.targets WHERE "PublicationPackageId"='pkg-good-keep') <> 2 THEN RAISE EXCEPTION 'targets not preserved'; END IF;
 IF (SELECT count(*) FROM sync.cursors WHERE "CursorKey"='package:pkg-good-keep') <> 2 THEN RAISE EXCEPTION 'keeper markers missing'; END IF;
 IF NOT EXISTS (SELECT 1 FROM sync.cursors WHERE "NodeId"='node-a' AND "CursorKey"='package:pkg-good-keep' AND "CursorValue"='value-a') THEN RAISE EXCEPTION 'removed-only node marker was not preserved'; END IF;
 IF NOT EXISTS (SELECT 1 FROM sync.cursors WHERE "NodeId"='node-b' AND "CursorKey"='package:pkg-good-keep' AND "CursorValue"='value-b-keep') THEN RAISE EXCEPTION 'existing keeper marker was not preserved'; END IF;
 IF EXISTS (SELECT 1 FROM sync.cursors WHERE "CursorKey"='package:pkg-good-drop') THEN RAISE EXCEPTION 'removed marker remains'; END IF;
 IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname='publication' AND indexname='IX_packages_ExamVersionId' AND indexdef ILIKE '%UNIQUE%') THEN RAISE EXCEPTION 'unique index missing'; END IF;
END $$;
SQL
dotnet ef database update "$previous" --project src/Central/PlanCope.Central.Migrations --startup-project src/Central/PlanCope.Central.Migrations
psql_in postgres -c "SELECT to_regclass('publication.\"IX_packages_ExamVersionId\"') IS NULL AS down_dropped_index;"
dotnet ef database update "$migration" --project src/Central/PlanCope.Central.Migrations --startup-project src/Central/PlanCope.Central.Migrations

psql_in postgres -c 'CREATE DATABASE divergent;'
dotnet ef database update "$previous" --project src/Central/PlanCope.Central.Migrations --startup-project src/Central/PlanCope.Central.Migrations --connection "Host=localhost;Port=$port;Database=divergent;Username=postgres;Password=test"
psql_in divergent <<'SQL'
INSERT INTO exam.exams ("Id","Code","Title","Status","CreatedAt","UpdatedAt") VALUES ('ex-bad','bad','Bad','Published',now(),now());
INSERT INTO exam.versions ("Id","ExamId","VersionNumber","SchemaVersion","Status","CreatedAt","UpdatedAt") VALUES ('ver-bad','ex-bad',1,1,'Published',now(),now());
INSERT INTO publication.packages ("Id","ExamVersionId","PackageVersion","Checksum","Manifest","Status","CreatedAt","PublishedAt") VALUES
('pkg-bad-keep','ver-bad',1,'checksum-one','{}','Published',now()-interval '2 days',now()-interval '2 days'),
('pkg-bad-drop','ver-bad',2,'checksum-two','{}','Published',now()-interval '1 day',now()-interval '1 day');
INSERT INTO sync.cursors ("Id","NodeId","CursorKey","CursorValue","UpdatedAt") VALUES
('cur-bad-a','node-bad','package:pkg-bad-keep','keep',now()),('cur-bad-b','node-bad-2','package:pkg-bad-drop','drop',now());
SQL
if dotnet ef database update "$migration" --project src/Central/PlanCope.Central.Migrations --startup-project src/Central/PlanCope.Central.Migrations --connection "$connection;Database=divergent" > "$divergent_log" 2>&1; then
  echo 'divergent checksums unexpectedly migrated' >&2; exit 1
fi
grep -F 'ver-bad' "$divergent_log"
psql_in divergent <<'SQL'
DO $$ BEGIN
 IF to_regclass('publication."IX_packages_ExamVersionId"') IS NOT NULL THEN RAISE EXCEPTION 'index should have rolled back'; END IF;
 IF (SELECT count(*) FROM publication.packages WHERE "ExamVersionId"='ver-bad') <> 2 THEN RAISE EXCEPTION 'packages should have rolled back'; END IF;
 IF (SELECT count(*) FROM sync.cursors WHERE "CursorKey" IN ('package:pkg-bad-keep','package:pkg-bad-drop')) <> 2 THEN RAISE EXCEPTION 'cursors should have rolled back'; END IF;
END $$;
SQL
echo 'PostgreSQL migration scenarios passed.'
