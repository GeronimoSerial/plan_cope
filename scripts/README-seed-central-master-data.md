# Central master data seed (Phase 1, dry run)

## Purpose

Phase 1 dry-run seed of Central master data — `core.provinces`, `core.departments`,
`core.localities`, and `core.schools` — extracted from
`asistencias.public.secciones` and loaded into `plan_cope` inside a single
transaction that always ends in `ROLLBACK`. Nothing is committed.

## Usage

The script must be executable (`./scripts/seed-central-master-data.sh`). The
caller supplies connection settings via the environment:

```bash
source /tmp/claude-1000/pg.env
./scripts/seed-central-master-data.sh
```

Standard `PG*` variables (`PGHOST`, `PGPORT`, `PGUSER`, `PGPASSWORD`,
`PGDATABASE`, `PGSSLMODE`) are read from the environment only. There is no
`psql` on the host; every call runs through the `postgres:17` image with those
variables passed into the container by name.

## What the pipeline does

1. **Extract** — five `COPY (...) TO STDOUT WITH (FORMAT csv)` queries against
   `asistencias.public.secciones`, each written to a temp CSV:
   `provinces`, `departments`, `localities`, `schools`, `cue_conflicts`.
   The `schools` query uses `DISTINCT ON (cue_anexo)` ordered by
   `ciclo_lectivo DESC NULLS LAST, id ASC` so the one cue with conflicting names
   resolves deterministically (highest cycle, then lowest id).
2. **Stage** — one `psql` session against `plan_cope` opens a transaction, creates
   five `TEMP` staging tables, and loads the CSVs via `\copy ... FROM STDIN`.
3. **Dedup insert** — `INSERT ... SELECT ... WHERE NOT EXISTS` per table, in FK
   order (provinces → departments → localities → schools), each in its own `DO`
   block that raises a `NOTICE` with the row count.
4. **Roll back** — the batch always ends with `ROLLBACK`.

## Natural keys

| Table          | Natural key                  |
| -------------- | ---------------------------- |
| `core.provinces`  | `Code`                     |
| `core.departments` | `(ProvinceId, Code)`      |
| `core.localities` | `(DepartmentId, Code)`     |
| `core.schools`    | `Cue` (normalized 9-digit)| |

`Code` values derive from the source chain (`jurisdiccion_id`,
`departamento_id`, `localidad_id`); the full parent chain is always used so
raw ids that collide across provinces/departments are never merged. The source
has 849 distinct full-chain localities — the shorter
`(departamento_id, localidad_id)` projection collapses to 578 only because
`departamento_id` is reused across provinces, which is exactly the merge the
full chain exists to prevent.

**Locality count note (849 staged → 848 inserted; not 578):** the work order
predicted 578 localities, but 578 is the count of distinct
`(departamento_id, localidad_id)` pairs — the exact cross-province merge the
full-chain rule exists to prevent. Live checks on `secciones` show 577 raw
`localidad_id` values, of which 151 appear in more than one province: distinct
`(jurisdiccion_id, localidad_id)` = distinct full chain = **849**. Staging only
578 rows would attach each collided id to a single province's department, and
the school insert (which joins localities on the full chain) would then silently
drop every school in the other province(s) for those ids. The full parent chain
is authoritative; of the 849 staged, 848 insert (province 33's single
NULL-geometry triple is skipped and reported).

## Column mapping (Phase 1, final)

- **core.provinces**: `Id` = new 32-char lowercase hex id
  (`replace(gen_random_uuid()::text, '-', '')`), `Code` = `jurisdiccion_id::text`,
  `Name` = `jurisdiccion`, `CreatedAt` = `now()`.
- **core.departments**: `Id` = hex, `Code` = `departamento_id::text`,
  `Name` = `departamento`, `ProvinceId` = matching province `Id` (joined on
  `Code = jurisdiccion_id::text`), `CreatedAt` = `now()`.
- **core.localities**: `Id` = hex, `Code` = `localidad_id::text`,
  `PostalCode` = `cp` (empty → `NULL`), `Name` = `localidad`, `DepartmentId` =
  matching department `Id` (joined on province `Code` + department `Code`),
  `CreatedAt` = `now()`.
- **core.schools**: `Id` = hex, `Code` = original raw `cue_anexo` string (e.g.
  `"1801175-00"`), `Cue` = normalized 9-digit bigint, `Annex` = last-2-digits
  integer, `Name` = `establecimiento_nombre`, `LocalityId` = matching locality
  `Id` (full chain: province + department + locality), `Status` = `'Active'`,
  `DeletedAt` = `NULL`, `CreatedAt`/`UpdatedAt` = `now()`. For the one
  `cue_anexo` with conflicting names, the row with the highest `ciclo_lectivo`
  (tie-break lowest `id`) wins; that cue is reported in the output.

## CUE normalization

Mirrors `CueCode.TryNormalize`:

- Strip every non-digit character: `regexp_replace(cue_anexo, '\D', '', 'g')`.
- The result must be exactly 9 digits; otherwise the row is skipped, counted,
  and up to 2 raw distinct examples are printed (no personal data in this
  column).
- `Cue` (`bigint`) = the 9-digit value (`1801175-00` → `180117500`).
- `Annex` (`integer`) = last 2 digits parsed as int (`'00'` → `0`).
- `Code` keeps the original raw string (`1801175-00`).

## Idempotency

`WHERE NOT EXISTS` on the natural key only — no unique constraints or indexes
are added (adding any is out of scope). `core.schools` already has a
pre-existing unique index on `Cue` (`IX_schools_Cue`), which acts as a backstop;
the `WHERE NOT EXISTS` clauses remain the dedup mechanism for all four tables.
Re-running the script never creates duplicate rows, so the logic is correct now
even though this batch always rolls back.

## Safety guarantees

- **Always `ROLLBACK`** — the SQL ends with an explicit `ROLLBACK`; there is no
  commit flag by design, and no `COMMIT` path exists in this batch.
- **Credentials from the environment only** — never hardcoded, never stored,
  never printed; the script does not reference the environment file's path.
- **Scope is exactly the four `core` tables** above. The script never touches
  `roster.*`, `core.users`, `core.user_roles`, or `core.user_schools`, and it
  never reads the minors data tables (`personas`, `alumnos`,
  `personas_alumnos_secciones`). The only source read is
  `asistencias.public.secciones`.
- **No extensions installed** and no schema changes (no `dblink`,
  `postgres_fdw`, constraints, or indexes).

## Expected dry-run output

Approximate `NOTICE` lines (counts may be lower if demo rows already exist in
`core.*`, because `WHERE NOT EXISTS` skips them):

```
NOTICE:  provinces: 5 row(s) would be inserted
NOTICE:  departments: 117 row(s) would be inserted
NOTICE:  localities: 848 row(s) would be inserted
NOTICE:  schools: 2058 row(s) would be inserted
NOTICE:  skipped malformed cue_anexo: 0 row(s)
NOTICE:  cue_anexo with conflicting names: 1
NOTICE:  conflicting-name cue_anexo: <the one cue_anexo>
NOTICE:  skipped provinces with NULL code/name: 0
NOTICE:  skipped departments with NULL code/name: 1
NOTICE:  skipped departments belong to province code(s): 33
NOTICE:  skipped localities with NULL code/name or incomplete chain: 1
NOTICE:  skipped schools with incomplete parent chain: 1
NOTICE:  skipped school cue_anexo values (up to 10): 1801096-00
ROLLBACK
DRY RUN — rolled back
```

Staged vs insertable: 118 departments are staged but one (province 33's) has
NULL `departamento_id`/`departamento`; 849 localities are staged but
province 33's single `(jurisdiccion_id, NULL, NULL)` triple has NULL code/name;
2,059 schools are staged but one cue (`1801096-00`) has an incomplete parent
chain. The school figure: all 6 incomplete raw rows share that single
`cue_anexo`, so the `DISTINCT ON (cue_anexo)` extraction collapses them to 1
staged row — 2,059 − 1 = 2,058. Those 1 + 1 + 1 staged rows are skipped and
reported rather than failing the batch on the NOT NULL constraints — the same
skip-and-report policy used for malformed `cue_anexo` values.
