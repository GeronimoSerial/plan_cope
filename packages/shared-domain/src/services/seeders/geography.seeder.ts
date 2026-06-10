/**
 * Geography seeder — central PostgreSQL.
 *
 * Creates a placeholder hierarchy for the Corrientes province:
 *   1 province → 3 departments → 6 localities → 6 schools → 12 classrooms
 *
 * All names are placeholders (per Fase 1 design — real data lives in
 * the production import pipeline, not in seeders). The hierarchy uses
 * FK-aware inserts in topological order so PG's FK constraints are
 * always satisfied.
 *
 * Idempotency: we use `ON CONFLICT (code) DO NOTHING` on every insert.
 * Re-running the seeder produces no duplicates and no errors.
 */
import { sql } from 'drizzle-orm';
import type { PostgresJsDatabase } from 'drizzle-orm/postgres-js';
import {
  classrooms,
  departments,
  localities,
  provinces,
  schools,
} from '../../db/central/schema.js';

// Re-exported for downstream consumers (and to keep tree-shaking honest).
export { sql };

const PROVINCE_CODE = 'W';
const PROVINCE_NAME = 'Corrientes (placeholder)';

const DEPARTMENTS_DEF: ReadonlyArray<{ code: string; name: string }> = [
  { code: '001', name: 'Capital' },
  { code: '002', name: 'Interior Norte' },
  { code: '003', name: 'Interior Sur' },
];

const LOCALITIES_PER_DEPT = 2;
const SCHOOLS_PER_LOCALITY = 1;
const CLASSROOMS_PER_SCHOOL = 2;

const SHIFTS = ['morning', 'afternoon'] as const;

export const seedGeography = async (
  db: PostgresJsDatabase,
): Promise<void> => {
  // 1. Province (top of hierarchy). The unique index on `code` makes
  //    this an upsert: a second call is a no-op.
  const [province] = await db
    .insert(provinces)
    .values({ code: PROVINCE_CODE, name: PROVINCE_NAME })
    .onConflictDoNothing({ target: provinces.code })
    .returning({ id: provinces.id });
  // After the upsert, re-read to get the id (handles the case where the
  // row already existed before this run).
  const provinceId =
    province?.id ??
    (
      await db.execute<{ id: string }>(
        sql`SELECT id FROM provinces WHERE code = ${PROVINCE_CODE} LIMIT 1`,
      )
    ).at(0)?.id;
  if (!provinceId) {
    throw new Error('seedGeography: failed to obtain province id');
  }

  // 2. Departments.
  for (const dept of DEPARTMENTS_DEF) {
    const [inserted] = await db
      .insert(departments)
      .values({ code: dept.code, name: dept.name, province_id: provinceId })
      .onConflictDoNothing({ target: [departments.province_id, departments.code] })
      .returning({ id: departments.id });
    const departmentId =
      inserted?.id ??
      (
        await db.execute<{ id: string }>(
          sql`SELECT id FROM departments WHERE province_id = ${provinceId} AND code = ${dept.code} LIMIT 1`,
        )
      ).at(0)?.id;
    if (!departmentId) continue;

    // 3. Localities (LOCALITIES_PER_DEPT per department).
    for (let lIdx = 0; lIdx < LOCALITIES_PER_DEPT; lIdx++) {
      const localityCode = `${dept.code}0${lIdx + 1}`;
      const localityName = `${dept.name} - Localidad ${lIdx + 1}`;
      const [lInserted] = await db
        .insert(localities)
        .values({
          department_id: departmentId,
          code: localityCode,
          name: localityName,
          postal_code: `W${dept.code}${lIdx + 1}00`,
        })
        .onConflictDoNothing({
          target: [localities.department_id, localities.code],
        })
        .returning({ id: localities.id });
      const localityId =
        lInserted?.id ??
        (
          await db.execute<{ id: string }>(
            sql`SELECT id FROM localities WHERE department_id = ${departmentId} AND code = ${localityCode} LIMIT 1`,
          )
        ).at(0)?.id;
      if (!localityId) continue;

      // 4. Schools (SCHOOLS_PER_LOCALITY per locality).
      for (let sIdx = 0; sIdx < SCHOOLS_PER_LOCALITY; sIdx++) {
        const schoolCode = `S${localityCode}${sIdx + 1}`;
        // `cue` is a BIGINT — use a stable, unique-looking 9-digit
        // value derived from the locality code.
        const cue = BigInt(`8200${dept.code}${lIdx + 1}${sIdx + 1}`);
        const [sInserted] = await db
          .insert(schools)
          .values({
            code: schoolCode,
            cue,
            name: `Escuela ${localityName}`,
            locality_id: localityId,
          })
          .onConflictDoNothing({
            target: [schools.locality_id, schools.code],
          })
          .returning({ id: schools.id });
        const schoolId =
          sInserted?.id ??
          (
            await db.execute<{ id: string }>(
              sql`SELECT id FROM schools WHERE locality_id = ${localityId} AND code = ${schoolCode} LIMIT 1`,
            )
          ).at(0)?.id;
        if (!schoolId) continue;

        // 5. Classrooms (CLASSROOMS_PER_SCHOOL per school).
        //    The `classrooms` table has no UNIQUE constraint, so
        //    `onConflictDoNothing()` is a no-op. We pre-check the count
        //    for the school and skip if classrooms already exist.
        const existing = await db.execute<{ n: number }>(
          sql`SELECT count(*)::int AS n FROM classrooms WHERE school_id = ${schoolId}`,
        );
        if ((existing.at(0)?.n ?? 0) >= CLASSROOMS_PER_SCHOOL) continue;
        for (let cIdx = 0; cIdx < CLASSROOMS_PER_SCHOOL; cIdx++) {
          const classroomCode = `C${schoolCode}${cIdx + 1}`;
          await db.insert(classrooms).values({
            school_id: schoolId,
            code: classroomCode,
            name: `Aula ${cIdx + 1}`,
            shift: SHIFTS[cIdx % SHIFTS.length],
          });
        }
      }
    }
  }
};
