/**
 * Database-agnostic migrator.
 *
 * Accepts either a Drizzle Postgres-js database or a Drizzle better-sqlite3
 * database and runs all pending SQL files in the given folder. Both
 * dialects expose the same `migrate()` helper; the type union is just for
 * caller convenience.
 *
 * The function returns a small audit object describing what was applied.
 * `drizzle-orm`'s built-in `migrate` is idempotent — it tracks applied
 * migrations in `__drizzle_migrations` — so a second call is a no-op.
 */
import { migrate } from 'drizzle-orm/better-sqlite3/migrator';
import { migrate as migratePg } from 'drizzle-orm/postgres-js/migrator';
import type { BetterSQLite3Database } from 'drizzle-orm/better-sqlite3';
import type { PostgresJsDatabase } from 'drizzle-orm/postgres-js';
import { readdirSync, statSync } from 'node:fs';
import { resolve } from 'node:path';

export type AnyDb = PostgresJsDatabase | BetterSQLite3Database;

export interface MigrationResult {
  /** Absolute path of the migrations folder, normalized. */
  folder: string;
  /** Names of all `.sql` files found (sorted). */
  files: string[];
  /** Dialect inferred from db constructor name (best-effort). */
  dialect: 'postgresql' | 'sqlite';
}

const listSqlFiles = (folder: string): string[] => {
  const abs = resolve(folder);
  const stat = statSync(abs);
  if (!stat.isDirectory()) {
    throw new Error(`migrations folder is not a directory: ${abs}`);
  }
  return readdirSync(abs)
    .filter((f) => f.endsWith('.sql'))
    .sort();
};

const inferDialect = (db: AnyDb): 'postgresql' | 'sqlite' => {
  const ctorName =
    (db as unknown as { constructor?: { name?: string } }).constructor?.name ?? '';
  if (
    ctorName.toLowerCase().includes('pg') ||
    ctorName.toLowerCase().includes('postgres') ||
    ctorName.toLowerCase().includes('postgresjs')
  ) {
    return 'postgresql';
  }
  if (
    ctorName.toLowerCase().includes('sqlite') ||
    ctorName.toLowerCase().includes('better')
  ) {
    return 'sqlite';
  }
  // Fallback: try sqlite first (cheaper to fail loudly)
  return 'sqlite';
};

/**
 * Run all pending migrations for the given db.
 *
 * @param db        A Drizzle database instance (Postgres-js or better-sqlite3).
 * @param folder    Absolute or relative path to the migrations folder.
 * @returns         A summary object describing what was found / applied.
 */
export const runMigrations = async (
  db: AnyDb,
  folder: string,
): Promise<MigrationResult> => {
  const abs = resolve(folder);
  const files = listSqlFiles(abs);
  const dialect = inferDialect(db);

  if (dialect === 'postgresql') {
    await migratePg(db as PostgresJsDatabase, { migrationsFolder: abs });
  } else {
    migrate(db as BetterSQLite3Database, { migrationsFolder: abs });
  }

  return { folder: abs, files, dialect };
};
