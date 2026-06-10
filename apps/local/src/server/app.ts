/**
 * apps/local — Fastify 5 HTTP server (SQLite WAL backend).
 *
 * Wires better-sqlite3 with the required PRAGMAs, runs migrations on boot,
 * and exposes `GET /api/health`.
 */
import Fastify, { type FastifyInstance } from 'fastify';
import Database from 'better-sqlite3';
import { drizzle } from 'drizzle-orm/better-sqlite3';
import type { BetterSQLite3Database } from 'drizzle-orm/better-sqlite3';
import { runMigrations } from '@plan-cope/shared-domain/db/migrator';
import { mkdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

const __dirname = dirname(fileURLToPath(import.meta.url));

const VERSION = '0.1.0-fase1';
const DEFAULT_MIGRATIONS = resolve(
  __dirname,
  '..',
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

export interface BuildLocalOptions {
  /** Pre-built Drizzle db (for tests). If omitted, opens a new SQLite file. */
  db?: BetterSQLite3Database;
  /** Skip auto-migration on boot (used by tests). */
  skipMigrations?: boolean;
  /** Migrations folder override. */
  migrationsFolder?: string;
  /** Logger level. */
  loggerLevel?: 'fatal' | 'error' | 'warn' | 'info' | 'debug' | 'trace';
}

const ensureFileDir = (path: string) => {
  if (path !== ':memory:') {
    try {
      mkdirSync(dirname(path), { recursive: true });
    } catch {
      // ignore — file might already exist or be in a non-writable location
    }
  }
};

export const buildLocalApp = async (
  opts: BuildLocalOptions = {},
): Promise<FastifyInstance> => {
  const app = Fastify({
    logger: { level: opts.loggerLevel ?? process.env['LOG_LEVEL'] ?? 'info' },
  });

  let db: BetterSQLite3Database;
  let sqliteHandle: Database.Database | null = null;
  if (opts.db) {
    db = opts.db;
  } else {
    const dbPath = process.env['LOCAL_DB_PATH'] ?? './data/local.db';
    ensureFileDir(dbPath);
    sqliteHandle = new Database(dbPath);
    // Required PRAGMAs from DDL_V3.sql preamble.
    sqliteHandle.pragma('journal_mode = WAL');
    sqliteHandle.pragma('foreign_keys = ON');
    sqliteHandle.pragma('busy_timeout = 5000');
    db = drizzle(sqliteHandle);
  }

  if (!opts.skipMigrations) {
    const folder = opts.migrationsFolder ?? DEFAULT_MIGRATIONS;
    app.log.info({ folder }, 'running local migrations');
    await runMigrations(db, folder);
  }

  app.decorate('db', db);

  app.get('/api/health', async () => {
    let dbStatus: 'connected' | 'error' = 'connected';
    try {
      // Ping the underlying SQLite client. Cast to access $client (the
      // abstract `BetterSQLite3Database` type doesn't expose it, but the
      // concrete instance returned by `drizzle()` does).
      const client = (db as unknown as { $client: Database.Database }).$client;
      client.prepare('SELECT 1 as ok').get();
    } catch (err) {
      app.log.error({ err }, 'sqlite health check failed');
      dbStatus = 'error';
    }
    return {
      status: dbStatus === 'connected' ? 'ok' : 'degraded',
      db: dbStatus,
      uptime: Math.round(process.uptime() * 100) / 100,
      version: VERSION,
    };
  });

  app.addHook('onClose', async () => {
    if (sqliteHandle) sqliteHandle.close();
  });

  return app;
};

const isMain = import.meta.url === `file://${process.argv[1]}`;
if (isMain) {
  const port = Number(process.env['PORT'] ?? 3001);
  buildLocalApp()
    .then((app) => app.listen({ port, host: '0.0.0.0' }))
    .then((addr) => {
      console.log(`local listening on ${addr}`);
    })
    .catch((err) => {
      console.error('local failed to start:', err);
      process.exit(1);
    });
}
