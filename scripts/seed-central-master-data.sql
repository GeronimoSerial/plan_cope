-- Seed Central master data (Phase 1) - DRY RUN ONLY.
--
-- Purpose: stage distinct provinces/departments/localities/schools extracted from
-- asistencias.public.secciones (spliced in by seed-central-master-data.sh at the
-- @@DATA markers) and preview dedup inserts into core.provinces, core.departments,
-- core.localities, and core.schools.
--
-- DRY-RUN GUARANTEE: this batch never commits. The file opens exactly one
-- transaction (BEGIN) and ends with an explicit ROLLBACK as its last statement.
-- There is NO COMMIT statement anywhere and no conditional commit path, ever.
-- The RAISE NOTICE row counts are "would insert" counts for THIS transaction and
-- disappear with the rollback.
--
-- PHASE 1 COLUMN MAPPING (final):
--   core.provinces:   Id = 32-char lowercase hex (replace(gen_random_uuid()::text, '-', '')),
--                     Code = jurisdiccion_id::text, Name = jurisdiccion, CreatedAt = now().
--   core.departments: Id = hex, Code = departamento_id::text, Name = departamento,
--                     ProvinceId = province joined on Code = jurisdiccion_id::text,
--                     CreatedAt = now().
--   core.localities:  Id = hex, Code = localidad_id::text, PostalCode = cp
--                     (empty -> NULL, column is nullable), Name = localidad,
--                     DepartmentId = department joined on province Code + department
--                     Code, CreatedAt = now().
--   core.schools:     Id = hex, Code = ORIGINAL raw cue_anexo string (e.g. "1801175-00"),
--                     Cue = normalized 9-digit bigint, Annex = last-2-digits int,
--                     Name = establecimiento_nombre, LocalityId = locality joined on
--                     the FULL chain (province Code + department Code + locality Code),
--                     Status = 'Active', DeletedAt = NULL, CreatedAt/UpdatedAt = now().
--                     The single cue_anexo with conflicting names was resolved during
--                     extraction (DISTINCT ON: highest ciclo_lectivo, tie-break lowest
--                     id) and is reported from staging_cue_name_conflicts below.
--
-- NATURAL KEYS: province Code; department (ProvinceId, Code); locality
-- (DepartmentId, Code); school Cue (normalized 9 digits).
--
-- IDEMPOTENCY: WHERE NOT EXISTS only. No unique constraints or indexes are added
-- (adding any is out of scope for this batch). Note: core.schools already carries
-- a pre-existing unique index on Cue (IX_schools_Cue, from the MakeSchoolsCueUnique
-- migration) -- the WHERE NOT EXISTS still does the dedup work; that index is only
-- a backstop. The other three natural keys have no constraint at all.
--
-- IDENTIFIER CASE: EF Core created the core.* columns as case-sensitive quoted
-- identifiers ("Id", "Code", "ProvinceId", ...). Every reference to a core column
-- below is double-quoted on purpose -- an unquoted Id would fold to id and miss
-- the column entirely.
--
-- NULL KEYS: a few source rows carry NULL department/locality geography (6 raw
-- rows, all in province 33; the DISTINCT ON extractions collapse them to 1
-- department row, 1 locality triple, and 1 school cue). Those rows cannot
-- satisfy the NOT NULL constraints on Code/Name, so each INSERT skips them
-- explicitly and the reporting section below counts them -- the same
-- skip-and-report policy the work order prescribes for malformed cue_anexo
-- values, applied so one bad source row never fails the whole load.

BEGIN;

-- Staging tables: every column text, order matches the CSV column order exactly.
CREATE TEMP TABLE staging_provinces (
  jurisdiccion_id text,
  jurisdiccion text
);

CREATE TEMP TABLE staging_departments (
  jurisdiccion_id text,
  departamento_id text,
  departamento text
);

CREATE TEMP TABLE staging_localities (
  jurisdiccion_id text,
  departamento_id text,
  localidad_id text,
  cp text,
  localidad text
);

CREATE TEMP TABLE staging_schools (
  cue_anexo text,
  jurisdiccion_id text,
  departamento_id text,
  localidad_id text,
  establecimiento_nombre text
);

CREATE TEMP TABLE staging_cue_name_conflicts (
  cue_anexo text
);

-- The @@DATA line is replaced by seed-central-master-data.sh with the matching
-- CSV file's contents; \copy then consumes those lines from the same stdin
-- stream up to \. (psql reads this file via -f -).
\copy staging_provinces FROM STDIN WITH (FORMAT csv)
@@DATA provinces@@
\.

\copy staging_departments FROM STDIN WITH (FORMAT csv)
@@DATA departments@@
\.

\copy staging_localities FROM STDIN WITH (FORMAT csv)
@@DATA localities@@
\.

\copy staging_schools FROM STDIN WITH (FORMAT csv)
@@DATA schools@@
\.

\copy staging_cue_name_conflicts FROM STDIN WITH (FORMAT csv)
@@DATA cue_conflicts@@
\.

-- Dedup inserts in FK order: provinces -> departments -> localities -> schools.
-- Each table gets its own DO block so the row count is single-sourced from
-- GET DIAGNOSTICS of that INSERT. Idempotency is WHERE NOT EXISTS on the natural
-- key only -- no constraints or indexes are added.

DO $$
DECLARE
  n bigint;
BEGIN
  INSERT INTO core.provinces ("Id", "Code", "Name", "CreatedAt")
  SELECT
    replace(gen_random_uuid()::text, '-', ''),
    s.jurisdiccion_id,
    s.jurisdiccion,
    now()
  FROM staging_provinces s
  WHERE s.jurisdiccion_id IS NOT NULL
    AND s.jurisdiccion IS NOT NULL
    AND NOT EXISTS (
      SELECT 1 FROM core.provinces p
      WHERE p."Code" = s.jurisdiccion_id
    );
  GET DIAGNOSTICS n = ROW_COUNT;
  RAISE NOTICE 'provinces: % row(s) would be inserted', n;
END $$;

DO $$
DECLARE
  n bigint;
BEGIN
  INSERT INTO core.departments ("Id", "Code", "Name", "ProvinceId", "CreatedAt")
  SELECT
    replace(gen_random_uuid()::text, '-', ''),
    s.departamento_id,
    s.departamento,
    p."Id",
    now()
  FROM staging_departments s
  JOIN core.provinces p ON p."Code" = s.jurisdiccion_id
  WHERE s.departamento_id IS NOT NULL
    AND s.departamento IS NOT NULL
    AND NOT EXISTS (
      SELECT 1 FROM core.departments d
      WHERE d."Code" = s.departamento_id
        AND d."ProvinceId" = p."Id"
    );
  GET DIAGNOSTICS n = ROW_COUNT;
  RAISE NOTICE 'departments: % row(s) would be inserted', n;
END $$;

DO $$
DECLARE
  n bigint;
BEGIN
  INSERT INTO core.localities ("Id", "DepartmentId", "Code", "PostalCode", "Name", "CreatedAt")
  SELECT
    replace(gen_random_uuid()::text, '-', ''),
    d."Id",
    s.localidad_id,
    NULLIF(s.cp, ''),
    s.localidad,
    now()
  FROM staging_localities s
  JOIN core.provinces p ON p."Code" = s.jurisdiccion_id
  JOIN core.departments d ON d."ProvinceId" = p."Id" AND d."Code" = s.departamento_id
  WHERE s.localidad_id IS NOT NULL
    AND s.localidad IS NOT NULL
    AND NOT EXISTS (
      SELECT 1 FROM core.localities l
      WHERE l."Code" = s.localidad_id
        AND l."DepartmentId" = d."Id"
    );
  GET DIAGNOSTICS n = ROW_COUNT;
  RAISE NOTICE 'localities: % row(s) would be inserted', n;
END $$;

DO $$
DECLARE
  n bigint;
BEGIN
  INSERT INTO core.schools ("Id", "Code", "Cue", "Annex", "Name", "LocalityId", "Status", "DeletedAt", "CreatedAt", "UpdatedAt")
  SELECT
    replace(gen_random_uuid()::text, '-', ''),
    s.cue_anexo,
    s.normalized::bigint,
    right(s.normalized, 2)::int,
    s.establecimiento_nombre,
    l."Id",
    'Active',
    NULL,
    now(),
    now()
  FROM (
    SELECT
      cue_anexo,
      jurisdiccion_id,
      departamento_id,
      localidad_id,
      establecimiento_nombre,
      normalized
    FROM (
      -- Normalization mirrors CueCode.TryNormalize: strip every non-digit; only
      -- exactly-9-digit results are eligible. The length filter lives inside this
      -- subquery so the ::bigint casts above can never see a malformed value
      -- (SQL gives no guarantee about conjunct evaluation order in an outer WHERE).
      SELECT
        cue_anexo,
        jurisdiccion_id,
        departamento_id,
        localidad_id,
        establecimiento_nombre,
        regexp_replace(cue_anexo, '\D', '', 'g') AS normalized
      FROM staging_schools
    ) raw
    WHERE length(raw.normalized) = 9
  ) s
  -- The full parent chain always exists: localities were extracted from the same
  -- source rows and inserted earlier in this same transaction.
  JOIN core.provinces p ON p."Code" = s.jurisdiccion_id
  JOIN core.departments d ON d."ProvinceId" = p."Id" AND d."Code" = s.departamento_id
  JOIN core.localities l ON l."DepartmentId" = d."Id" AND l."Code" = s.localidad_id
  WHERE s.jurisdiccion_id IS NOT NULL
    AND s.departamento_id IS NOT NULL
    AND s.localidad_id IS NOT NULL
    AND s.establecimiento_nombre IS NOT NULL
    AND NOT EXISTS (
      SELECT 1 FROM core.schools sc
      WHERE sc."Cue" = s.normalized::bigint
    );
  GET DIAGNOSTICS n = ROW_COUNT;
  RAISE NOTICE 'schools: % row(s) would be inserted', n;
END $$;

-- Reporting: skipped malformed cue_anexo values (expect 0), with up to 2 raw
-- distinct examples when any exist (this column holds no personal data).
DO $$
DECLARE
  malformed_count bigint;
  example_list text;
BEGIN
  SELECT count(*)
  INTO malformed_count
  FROM (
    SELECT cue_anexo, regexp_replace(cue_anexo, '\D', '', 'g') AS normalized
    FROM staging_schools
  ) s
  WHERE s.normalized IS NULL OR length(s.normalized) <> 9;

  RAISE NOTICE 'skipped malformed cue_anexo: % row(s)', malformed_count;

  IF malformed_count > 0 THEN
    SELECT string_agg(cue_anexo, ', ' ORDER BY cue_anexo)
    INTO example_list
    FROM (
      SELECT s.cue_anexo
      FROM (
        SELECT cue_anexo, regexp_replace(cue_anexo, '\D', '', 'g') AS normalized
        FROM staging_schools
      ) s
      WHERE s.normalized IS NULL OR length(s.normalized) <> 9
      ORDER BY s.cue_anexo
      LIMIT 2
    ) examples;
    RAISE NOTICE 'malformed cue_anexo examples (up to 2): %', example_list;
  END IF;
END $$;

-- Reporting: cue_anexo values whose source rows disagree on
-- establecimiento_nombre (expect exactly 1, already resolved during extraction).
DO $$
DECLARE
  conflict_count bigint;
  cue_val text;
BEGIN
  SELECT count(*) INTO conflict_count FROM staging_cue_name_conflicts;
  RAISE NOTICE 'cue_anexo with conflicting names: %', conflict_count;

  FOR cue_val IN
    SELECT cue_anexo FROM staging_cue_name_conflicts ORDER BY cue_anexo
  LOOP
    RAISE NOTICE 'conflicting-name cue_anexo: %', cue_val;
  END LOOP;
END $$;

-- Reporting: source rows skipped because their geography is incomplete (NULL
-- natural key and/or NULL required name), counted so the would-insert counts
-- above stay explainable instead of silently low. Known in the current source:
-- 6 incomplete raw rows, all in province 33, collapse (via the DISTINCT ON
-- extractions) to 1 department, 1 locality, and 1 school cue skipped.
-- Reported values are source codes/cues only -- no personal data is attached
-- to any of these columns.
DO $$
DECLARE
  n bigint;
  id_list text;
BEGIN
  SELECT count(*) INTO n
  FROM staging_provinces
  WHERE jurisdiccion_id IS NULL OR jurisdiccion IS NULL;
  RAISE NOTICE 'skipped provinces with NULL code/name: %', n;

  SELECT count(*) INTO n
  FROM staging_departments
  WHERE jurisdiccion_id IS NULL OR departamento_id IS NULL OR departamento IS NULL;
  RAISE NOTICE 'skipped departments with NULL code/name: %', n;
  IF n > 0 THEN
    SELECT string_agg(jurisdiccion_id, ', ' ORDER BY jurisdiccion_id) INTO id_list
    FROM (
      SELECT DISTINCT jurisdiccion_id
      FROM staging_departments
      WHERE jurisdiccion_id IS NULL OR departamento_id IS NULL OR departamento IS NULL
    ) affected;
    RAISE NOTICE 'skipped departments belong to province code(s): %', id_list;
  END IF;

  SELECT count(*) INTO n
  FROM staging_localities
  WHERE jurisdiccion_id IS NULL OR departamento_id IS NULL
     OR localidad_id IS NULL OR localidad IS NULL;
  RAISE NOTICE 'skipped localities with NULL code/name or incomplete chain: %', n;

  SELECT count(*) INTO n
  FROM staging_schools
  WHERE jurisdiccion_id IS NULL OR departamento_id IS NULL OR localidad_id IS NULL;
  RAISE NOTICE 'skipped schools with incomplete parent chain: %', n;
  IF n > 0 THEN
    SELECT string_agg(cue_anexo, ', ' ORDER BY cue_anexo) INTO id_list
    FROM (
      SELECT cue_anexo
      FROM staging_schools
      WHERE jurisdiccion_id IS NULL OR departamento_id IS NULL OR localidad_id IS NULL
      ORDER BY cue_anexo
      LIMIT 10
    ) affected;
    RAISE NOTICE 'skipped school cue_anexo values (up to 10): %', id_list;
  END IF;
END $$;

-- Always the last statement: this batch never commits.
ROLLBACK;
