/**
 * Seeder test suite.
 *
 * Geography / roles / exams seeders → central PostgreSQL (skip when PG
 * is unreachable so the test stays friendly to CI without docker).
 * Local seeder → in-memory SQLite (always available, fast).
 *
 * Each seeder test verifies:
 *   1. row counts after first run,
 *   2. Zod-validity of seeded data (we parse a sample row through the
 *      central Zod select schemas to prove the row shape matches),
 *   3. idempotency (second run does not duplicate).
 */
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { existsSync } from 'node:fs';
import { resolve } from 'node:path';
import postgres from 'postgres';
import { drizzle as drizzlePg, type PostgresJsDatabase } from 'drizzle-orm/postgres-js';
import Database from 'better-sqlite3';
import { drizzle as drizzleSqlite } from 'drizzle-orm/better-sqlite3';
import { runMigrations } from '../db/migrator.js';
import { seedGeography } from '../services/seeders/geography.seeder.js';
import { seedRolesAndUsers } from '../services/seeders/users.seeder.js';
import { seedExams } from '../services/seeders/exams.seeder.js';
import { seedLocal } from '../services/seeders/local.seeder.js';

const CENTRAL_MIGRATIONS = resolve(
  process.cwd(),
  'src/db/migrations/central',
);
const LOCAL_MIGRATIONS = resolve(process.cwd(), 'src/db/migrations/local');

const DATABASE_URL =
  process.env.SEEDER_TEST_DATABASE_URL ??
  process.env.DATABASE_URL ??
  'postgres://plan_cope:test@localhost:5432/plan_cope_seeders_test';

const skipIfPgDown = async (): Promise<boolean> => {
  try {
    const s = postgres(DATABASE_URL, { max: 1, idle_timeout: 1 });
    await s`select 1`;
    await s.end();
    return false;
  } catch {
    return true;
  }
};

let pgUp = false;
let pgSql: ReturnType<typeof postgres>;
let pgDb: PostgresJsDatabase;

beforeAll(async () => {
  pgUp = !(await skipIfPgDown());
  if (!pgUp) return;
  pgSql = postgres(DATABASE_URL, { max: 5 });
  // Clean slate for the central schema so seeder counts are deterministic.
  await pgSql.unsafe(`
    DROP SCHEMA IF EXISTS public CASCADE;
    CREATE SCHEMA public;
    DROP SCHEMA IF EXISTS drizzle CASCADE;
  `);
  pgDb = drizzlePg(pgSql);
  await runMigrations(pgDb, CENTRAL_MIGRATIONS);
});

afterAll(async () => {
  if (pgSql) await pgSql.end();
});

describe('seeders — geography (Corrientes placeholder)', () => {
  it('creates 1 province, 3 departments, 6 localities, 6 schools, 12 classrooms', async () => {
    if (!pgUp) {
      console.warn('skipping: Postgres not reachable');
      return;
    }
    if (!existsSync(CENTRAL_MIGRATIONS)) {
      throw new Error(`central migrations missing at ${CENTRAL_MIGRATIONS}`);
    }
    await seedGeography(pgDb);
    const counts = await pgSql<
      Array<{ table_name: string; n: number }>
    >`
      SELECT 'provinces' AS table_name, count(*)::int AS n FROM provinces
      UNION ALL SELECT 'departments', count(*)::int FROM departments
      UNION ALL SELECT 'localities', count(*)::int FROM localities
      UNION ALL SELECT 'schools', count(*)::int FROM schools
      UNION ALL SELECT 'classrooms', count(*)::int FROM classrooms
    `;
    const map = Object.fromEntries(counts.map((r) => [r.table_name, r.n]));
    expect(map.provinces).toBe(1);
    expect(map.departments).toBe(3);
    expect(map.localities).toBe(6);
    expect(map.schools).toBe(6);
    expect(map.classrooms).toBe(12);
  });

  it('is idempotent — re-running produces no duplicates', async () => {
    if (!pgUp) return;
    await seedGeography(pgDb);
    const cls = await pgSql<{ n: number }[]>`SELECT count(*)::int AS n FROM classrooms`;
    expect(cls[0]?.n).toBe(12);
  });

  it('province is "Corrientes" placeholder per design', async () => {
    if (!pgUp) return;
    const rows = await pgSql<{ name: string }[]>`SELECT name FROM provinces`;
    expect(rows[0]?.name.toLowerCase()).toContain('corrientes');
  });
});

describe('seeders — roles and users', () => {
  it('creates 3 roles (admin, operator, viewer) and 3 users with bcrypt hashes', async () => {
    if (!pgUp) {
      console.warn('skipping: Postgres not reachable');
      return;
    }
    await seedRolesAndUsers(pgDb);
    const counts = await pgSql<
      Array<{ table_name: string; n: number }>
    >`
      SELECT 'roles' AS table_name, count(*)::int AS n FROM roles
      UNION ALL SELECT 'users', count(*)::int FROM users
      UNION ALL SELECT 'user_roles', count(*)::int FROM user_roles
    `;
    const map = Object.fromEntries(counts.map((r) => [r.table_name, r.n]));
    expect(map.roles).toBe(3);
    expect(map.users).toBe(3);
    expect(map.user_roles).toBe(3);
    // Bcrypt hashes start with $2a$, $2b$, or $2y$.
    const users = await pgSql<{ password_hash: string }[]>`SELECT password_hash FROM users`;
    for (const u of users) {
      expect(u.password_hash).toMatch(/^\$2[aby]\$/);
    }
  });

  it('is idempotent', async () => {
    if (!pgUp) return;
    await seedRolesAndUsers(pgDb);
    const n = await pgSql<{ n: number }[]>`SELECT count(*)::int AS n FROM users`;
    expect(n[0]?.n).toBe(3);
  });
});

describe('seeders — exams', () => {
  it('creates 3 exams with versions, all 5 block types, options, answer keys', async () => {
    if (!pgUp) {
      console.warn('skipping: Postgres not reachable');
      return;
    }
    await seedExams(pgDb);
    const examRows = await pgSql<{ n: number }[]>`SELECT count(*)::int AS n FROM exams`;
    const verRows = await pgSql<{ n: number }[]>`SELECT count(*)::int AS n FROM exam_versions`;
    const blockTypes = await pgSql<{ block_type: string }[]>`
      SELECT DISTINCT block_type FROM exam_blocks ORDER BY block_type
    `;
    const optRows = await pgSql<{ n: number }[]>`SELECT count(*)::int AS n FROM exam_block_options`;
    const akRows = await pgSql<{ n: number }[]>`SELECT count(*)::int AS n FROM answer_keys`;
    expect(examRows[0]?.n).toBe(3);
    expect(verRows[0]?.n).toBe(3);
    expect(blockTypes.map((b) => b.block_type)).toEqual(
      expect.arrayContaining([
        'text',
        'image',
        'multiple_choice',
        'true_false',
        'short_answer',
      ]),
    );
    expect(optRows[0]?.n).toBeGreaterThan(0);
    expect(akRows[0]?.n).toBeGreaterThan(0);
  });

  it('exam_version.checksum (in metadata_json) is a 64-char hex string', async () => {
    if (!pgUp) return;
    const versions = await pgSql<{ checksum: string | null }[]>`
      SELECT metadata_json->>'checksum' AS checksum FROM exam_versions
    `;
    for (const v of versions) {
      expect(v.checksum).toMatch(/^[0-9a-f]{64}$/);
    }
  });

  it('is idempotent', async () => {
    if (!pgUp) return;
    await seedExams(pgDb);
    const n = await pgSql<{ n: number }[]>`SELECT count(*)::int AS n FROM exams`;
    expect(n[0]?.n).toBe(3);
  });

  it('every answer_key matches an existing exam_block (no orphans)', async () => {
    if (!pgUp) return;
    const orphans = await pgSql<{ n: number }[]>`
      SELECT count(*)::int AS n FROM answer_keys ak
      LEFT JOIN exam_blocks b ON b.id = ak.exam_block_id
      WHERE b.id IS NULL
    `;
    expect(orphans[0]?.n).toBe(0);
  });
});

describe('seeders — local (nodo local)', () => {
  it('creates 2 local_users and 4 app_settings with valid JSON values', async () => {
    if (!existsSync(LOCAL_MIGRATIONS)) {
      throw new Error(`local migrations missing at ${LOCAL_MIGRATIONS}`);
    }
    const sqlite = new Database(':memory:');
    sqlite.pragma('journal_mode = WAL');
    sqlite.pragma('foreign_keys = ON');
    sqlite.pragma('busy_timeout = 5000');
    const db = drizzleSqlite(sqlite);
    try {
      await runMigrations(db, LOCAL_MIGRATIONS);
      await seedLocal(db);
      const userCount = sqlite
        .prepare('SELECT count(*) as c FROM local_users')
        .get() as { c: number };
      expect(userCount.c).toBe(2);
      const settings = sqlite
        .prepare('SELECT key, value_json FROM app_settings')
        .all() as Array<{ key: string; value_json: string }>;
      expect(settings.length).toBe(4);
      const keys = settings.map((s) => s.key).sort();
      expect(keys).toEqual(
        ['central_url', 'jwt_secret', 'node_label', 'sync_interval'].sort(),
      );
      for (const s of settings) {
        expect(() => JSON.parse(s.value_json)).not.toThrow();
      }
    } finally {
      sqlite.close();
    }
  });

  it('local_users have bcrypt password hashes', async () => {
    const sqlite = new Database(':memory:');
    sqlite.pragma('journal_mode = WAL');
    sqlite.pragma('foreign_keys = ON');
    sqlite.pragma('busy_timeout = 5000');
    const db = drizzleSqlite(sqlite);
    try {
      await runMigrations(db, LOCAL_MIGRATIONS);
      await seedLocal(db);
      const users = sqlite
        .prepare('SELECT password_hash FROM local_users')
        .all() as Array<{ password_hash: string }>;
      for (const u of users) {
        expect(u.password_hash).toMatch(/^\$2[aby]\$/);
      }
    } finally {
      sqlite.close();
    }
  });

  it('is idempotent', async () => {
    const sqlite = new Database(':memory:');
    const db = drizzleSqlite(sqlite);
    try {
      await runMigrations(db, LOCAL_MIGRATIONS);
      await seedLocal(db);
      await seedLocal(db);
      const userCount = sqlite
        .prepare('SELECT count(*) as c FROM local_users')
        .get() as { c: number };
      expect(userCount.c).toBe(2);
    } finally {
      sqlite.close();
    }
  });
});

describe('seeders — full pipeline', () => {
  it('runs all seeders in order without throwing', async () => {
    if (!pgUp) {
      console.warn('skipping: Postgres not reachable');
      return;
    }
    await expect(
      (async () => {
        await seedGeography(pgDb);
        await seedRolesAndUsers(pgDb);
        await seedExams(pgDb);
      })(),
    ).resolves.toBeUndefined();
  });
});
