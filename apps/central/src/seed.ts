/**
 * apps/central — seed entry point.
 *
 * Run with:  `pnpm --filter @plan-cope/central db:seed`
 *
 * Connects to PostgreSQL using `DATABASE_URL`, applies migrations
 * (idempotent — drizzle tracks them in `__drizzle_migrations`), then
 * runs the four central seeders in topological order:
 *   1. seedGeography    — Corrientes placeholder (1 province + 3
 *                         departments + 6 localities + 6 schools +
 *                         12 classrooms)
 *   2. seedRolesAndUsers — 3 roles (admin / operator / viewer) + 3 users
 *   3. seedExams        — 3 exams with versions, blocks (all 5 types),
 *                         options and answer keys
 *
 * All seeders are idempotent — re-running is a no-op.
 */
import { config } from 'dotenv';
config();

import postgres from 'postgres';
import { drizzle } from 'drizzle-orm/postgres-js';
import { runMigrations } from '@plan-cope/shared-domain/db/migrator';
import {
  seedExams,
  seedGeography,
  seedRolesAndUsers,
} from '@plan-cope/shared-domain';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

const __dirname = dirname(fileURLToPath(import.meta.url));

const DEFAULT_MIGRATIONS = resolve(
  __dirname,
  '..',
  '..',
  '..',
  'packages',
  'shared-domain',
  'src',
  'db',
  'migrations',
  'central',
);

const url = process.env.DATABASE_URL;
if (!url) {
  console.error('apps/central: DATABASE_URL is required to run seeders.');
  process.exit(1);
}

const sql = postgres(url, { max: 5 });
const db = drizzle(sql);

const main = async () => {
  console.log('central: applying migrations (idempotent)…');
  await runMigrations(db, DEFAULT_MIGRATIONS);
  // `--migrate-only` runs migrations but skips the seeders. Useful in
  // CI where we just want a clean schema.
  if (!process.argv.includes('--migrate-only')) {
    console.log('central: seeding geography…');
    await seedGeography(db);
    console.log('central: seeding roles + users…');
    await seedRolesAndUsers(db);
    console.log('central: seeding exams…');
    await seedExams(db);
  } else {
    console.log('central: --migrate-only set, skipping seeders.');
  }
  console.log('central: done.');
  await sql.end();
};

main().catch((err) => {
  console.error('central: seed failed:', err);
  sql.end().catch(() => {});
  process.exit(1);
});
