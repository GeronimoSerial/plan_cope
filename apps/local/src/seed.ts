/**
 * apps/local — seed entry point.
 *
 * Run with:  `pnpm --filter @plan-cope/local db:seed`
 *
 * Connects to the local SQLite database (file at `LOCAL_DB_PATH` or
 * `./data/local.db` by default), applies migrations, and runs the
 * local seeder:
 *   - seedLocal  — 2 local_users (admin / operator) + 4 app_settings
 *                  (central_url, jwt_secret, sync_interval, node_label)
 *
 * Idempotent — re-running is a no-op.
 */
import { config } from 'dotenv';
config();

import Database from 'better-sqlite3';
import { drizzle } from 'drizzle-orm/better-sqlite3';
import { runMigrations } from '@plan-cope/shared-domain/db/migrator';
import { seedLocal } from '@plan-cope/shared-domain';
import { mkdirSync } from 'node:fs';
import { dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { resolve } from 'node:path';

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
  'local',
);

const dbPath = process.env.LOCAL_DB_PATH ?? './data/local.db';
if (dbPath !== ':memory:') {
  mkdirSync(dirname(dbPath), { recursive: true });
}

const sqlite = new Database(dbPath);
// DDL_V3 PRAGMAs (must match the central app's settings for the
// local node).
sqlite.pragma('journal_mode = WAL');
sqlite.pragma('foreign_keys = ON');
sqlite.pragma('busy_timeout = 5000');

const db = drizzle(sqlite);

const main = async () => {
  console.log('local: applying migrations (idempotent)…');
  await runMigrations(db, DEFAULT_MIGRATIONS);
  console.log('local: seeding local_users + app_settings…');
  await seedLocal(db);
  console.log('local: seed complete.');
  sqlite.close();
};

main().catch((err) => {
  console.error('local: seed failed:', err);
  try { sqlite.close(); } catch {}
  process.exit(1);
});
