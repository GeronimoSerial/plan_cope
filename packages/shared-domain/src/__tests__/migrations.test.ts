import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { existsSync } from 'node:fs';
import { resolve } from 'node:path';
import postgres from 'postgres';
import { drizzle as drizzlePg } from 'drizzle-orm/postgres-js';
import Database from 'better-sqlite3';
import { drizzle as drizzleSqlite } from 'drizzle-orm/better-sqlite3';
import { runMigrations } from '../db/migrator.js';
import * as centralSchema from '../db/central/schema.js';
import * as localSchema from '../db/local/schema.js';

const CENTRAL_MIGRATIONS = resolve(
  process.cwd(),
  'src/db/migrations/central',
);
const LOCAL_MIGRATIONS = resolve(process.cwd(), 'src/db/migrations/local');

const DATABASE_URL =
  process.env.DATABASE_URL ??
  'postgres://plan_cope:test@localhost:5432/plan_cope_test';

const skipIfPgDown = async (): Promise<boolean> => {
  try {
    const sql = postgres(DATABASE_URL, { max: 1, idle_timeout: 1 });
    await sql`select 1`;
    await sql.end();
    return false;
  } catch {
    return true;
  }
};

const skipIfNoMigrations = (folder: string): boolean => !existsSync(folder);

describe('shared-domain integration: migrations', () => {
  describe('central (PostgreSQL)', () => {
    let skipPg = false;
    let sql: ReturnType<typeof postgres>;

    beforeAll(async () => {
      skipPg = await skipIfPgDown();
      if (skipPg) return;
      sql = postgres(DATABASE_URL, { max: 5 });
      // Clean slate: drop all tables in public schema to start fresh
      await sql.unsafe(`
        DROP SCHEMA IF EXISTS public CASCADE;
        CREATE SCHEMA public;
        DROP SCHEMA IF EXISTS drizzle CASCADE;
      `);
    });

    afterAll(async () => {
      if (sql) await sql.end();
    });

    it('applies all migrations without error', async () => {
      if (skipPg) {
        console.warn('skipping: Postgres not reachable at', DATABASE_URL);
        return;
      }
      if (skipIfNoMigrations(CENTRAL_MIGRATIONS)) {
        throw new Error(
          `central migrations not found at ${CENTRAL_MIGRATIONS}; run \`pnpm db:generate\``,
        );
      }
      const db = drizzlePg(sql);
      await expect(runMigrations(db, CENTRAL_MIGRATIONS)).resolves.toBeDefined();
    });

    it('creates 26 central tables matching DDL_V3.sql', async () => {
      if (skipPg) return;
      const expected = [
        'users',
        'roles',
        'user_roles',
        'provinces',
        'departments',
        'localities',
        'schools',
        'classrooms',
        'exams',
        'exam_versions',
        'exam_blocks',
        'exam_block_options',
        'answer_keys',
        'exam_assets',
        'asset_usages',
        'publication_packages',
        'publication_targets',
        'registered_nodes',
        'central_delivery_sessions',
        'received_student_attempts',
        'received_submission_answers',
        'sync_inbox',
        'sync_cursors',
        'sync_attempts',
        'audit_logs',
        'settings',
      ];
      const rows = await sql<{ table_name: string }[]>`
        SELECT table_name FROM information_schema.tables
        WHERE table_schema = 'public' AND table_type = 'BASE TABLE'
        ORDER BY table_name
      `;
      const actual = rows.map((r) => r.table_name);
      // Drizzle's __drizzle_migrations table is internal — exclude it
      const userTables = actual.filter(
        (t) => t !== '__drizzle_migrations' && t !== 'drizzle',
      );
      expect(userTables.sort()).toEqual([...expected].sort());
      expect(userTables.length).toBe(26);
    });

    it('enforces CHECK constraints (users.status)', async () => {
      if (skipPg) return;
      await expect(
        sql`
          INSERT INTO users (email, password_hash, full_name, status)
          VALUES ('x@x.com', 'h', 'n', 'invalid_status')
        `,
      ).rejects.toThrow();
    });

    it('enforces FK cascade (delete province → departments)', async () => {
      if (skipPg) return;
      // Insert a province with a department, then delete the province
      const provinceRows = await sql<{ id: string }[]>`
        INSERT INTO provinces (code, name) VALUES ('TST', 'Test Province')
        RETURNING id
      `;
      const province = provinceRows[0];
      if (!province) throw new Error('province insert returned no rows');
      await sql`
        INSERT INTO departments (code, name, province_id)
        VALUES ('TST-DEP', 'Test Dept', ${province.id})
      `;
      const before = await sql<{ count: number }[]>`
        SELECT count(*)::int FROM departments WHERE province_id = ${province.id}
      `;
      expect(before[0]?.count).toBe(1);
      await sql`DELETE FROM provinces WHERE id = ${province.id}`;
      const after = await sql<{ count: number }[]>`
        SELECT count(*)::int FROM departments WHERE province_id = ${province.id}
      `;
      expect(after[0]?.count).toBe(0);
    });

    it('exports centralSchema with 26 table objects', () => {
      // centralSchema is the Drizzle schema module; verify it has table keys
      // (table objects are functions, not plain objects, so we count keys)
      const keys = Object.keys(centralSchema);
      const expectedKeys = [
        'users',
        'roles',
        'userRoles',
        'provinces',
        'departments',
        'localities',
        'schools',
        'classrooms',
        'exams',
        'examVersions',
        'examBlocks',
        'examBlockOptions',
        'answerKeys',
        'examAssets',
        'assetUsages',
        'publicationPackages',
        'publicationTargets',
        'registeredNodes',
        'centralDeliverySessions',
        'receivedStudentAttempts',
        'receivedSubmissionAnswers',
        'syncInbox',
        'syncCursors',
        'syncAttempts',
        'auditLogs',
        'settings',
      ];
      expect(keys.sort()).toEqual([...expectedKeys].sort());
    });
  });

  describe('local (SQLite)', () => {
    it('applies all migrations to :memory: without error', async () => {
      if (skipIfNoMigrations(LOCAL_MIGRATIONS)) {
        throw new Error(
          `local migrations not found at ${LOCAL_MIGRATIONS}; run \`pnpm db:generate\``,
        );
      }
      const sqlite = new Database(':memory:');
      try {
        const db = drizzleSqlite(sqlite);
        // SQLite migrate is synchronous; await Promise.resolve for symmetry
        await expect(runMigrations(db, LOCAL_MIGRATIONS)).resolves.toBeDefined();
      } finally {
        sqlite.close();
      }
    });

    it('creates 13 local tables matching DDL_V3.sql', async () => {
      if (skipIfNoMigrations(LOCAL_MIGRATIONS)) {
        throw new Error(
          `local migrations not found at ${LOCAL_MIGRATIONS}; run \`pnpm db:generate\``,
        );
      }
      const sqlite = new Database(':memory:');
      try {
        const db = drizzleSqlite(sqlite);
        await runMigrations(db, LOCAL_MIGRATIONS);
        const rows = sqlite
          .prepare(
            `SELECT name FROM sqlite_master WHERE type='table' ORDER BY name`,
          )
          .all() as Array<{ name: string }>;
        const actual = rows
          .map((r) => r.name)
          .filter((n) => n !== '__drizzle_migrations' && !n.startsWith('sqlite_'));
        const expected = [
          'app_settings',
          'delivery_sessions',
          'local_answer_keys',
          'local_assets',
          'local_audit_logs',
          'local_exam_blocks',
          'local_exam_versions',
          'local_users',
          'student_attempts',
          'submission_answers',
          'sync_attempts',
          'sync_outbox',
          'sync_state',
        ];
        expect(actual.sort()).toEqual([...expected].sort());
        expect(actual.length).toBe(13);
      } finally {
        sqlite.close();
      }
    });

    it('exports localSchema with 13 table objects', () => {
      const keys = Object.keys(localSchema);
      const expectedKeys = [
        'localUsers',
        'localExamVersions',
        'localExamBlocks',
        'localAnswerKeys',
        'localAssets',
        'deliverySessions',
        'studentAttempts',
        'submissionAnswers',
        'syncOutbox',
        'syncState',
        'syncAttempts',
        'localAuditLogs',
        'appSettings',
      ];
      expect(keys.sort()).toEqual([...expectedKeys].sort());
    });
  });

  describe('migrator robustness', () => {
    it('returns applied migrations metadata', async () => {
      if (await skipIfPgDown()) return;
      const sql2 = postgres(DATABASE_URL, { max: 2 });
      try {
        const db = drizzlePg(sql2);
        const result = await runMigrations(db, CENTRAL_MIGRATIONS);
        expect(result).toBeDefined();
      } finally {
        await sql2.end();
      }
    });

    it('migrations folder exists on disk', () => {
      // Sanity: a non-empty migrations folder means drizzle-kit generate ran
      expect(existsSync(CENTRAL_MIGRATIONS)).toBe(true);
      expect(existsSync(LOCAL_MIGRATIONS)).toBe(true);
    });
  });
});

// Suppress unused import warning when test files are listed in tools that
// don't understand the integration test (drizzle-kit may try to load schemas
// at module level in the future; keep the import here for safety).
