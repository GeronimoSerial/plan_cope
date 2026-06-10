/**
 * apps/central — Fastify 5 HTTP server.
 *
 * Wires the postgres-js connection, runs pending migrations on boot
 * (via `runMigrations` from `@plan-cope/shared-domain`), and exposes
 * `GET /api/health` for the orchestrator / smoke tests.
 */
import Fastify, { type FastifyInstance } from 'fastify';
import postgres from 'postgres';
import type { PostgresJsDatabase } from 'drizzle-orm/postgres-js';
import { drizzle } from 'drizzle-orm/postgres-js';
import { runMigrations } from '@plan-cope/shared-domain/db/migrator';
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
  'central',
);

export interface BuildOptions {
  /** Pre-built Drizzle db (for tests). If omitted, opens a new postgres-js connection. */
  db?: PostgresJsDatabase;
  /** Skip auto-migration on boot (used by tests that pre-applied migrations). */
  skipMigrations?: boolean;
  /** Migrations folder override. */
  migrationsFolder?: string;
  /** Logger level. Default 'info'. */
  loggerLevel?: 'fatal' | 'error' | 'warn' | 'info' | 'debug' | 'trace';
}

export const buildCentralApp = async (
  opts: BuildOptions = {},
): Promise<FastifyInstance> => {
  const app = Fastify({
    logger: { level: opts.loggerLevel ?? process.env['LOG_LEVEL'] ?? 'info' },
  });

  // 1. Acquire a Drizzle db (postgres-js under the hood).
  let db: PostgresJsDatabase;
  let pgClose: (() => Promise<void>) | null = null;
  if (opts.db) {
    db = opts.db;
  } else {
    const url = process.env['DATABASE_URL'];
    if (!url) {
      throw new Error('DATABASE_URL is required when no db is provided');
    }
    const client = postgres(url, { max: 10 });
    pgClose = () => client.end();
    db = drizzle(client);
  }

  // 2. Run pending migrations unless explicitly skipped.
  if (!opts.skipMigrations) {
    const folder = opts.migrationsFolder ?? DEFAULT_MIGRATIONS;
    app.log.info({ folder }, 'running central migrations');
    await runMigrations(db, folder);
  }

  // 3. Decorate Fastify with the db so routes/plugins can access it.
  app.decorate('db', db);

  // 4. Health endpoint.
  app.get('/api/health', async () => {
    let dbStatus: 'connected' | 'error' = 'connected';
    try {
      // Lightweight ping: select 1 — verifies the connection is alive.
      await db.execute('select 1 as ok');
    } catch (err) {
      app.log.error({ err }, 'db health check failed');
      dbStatus = 'error';
    }
    return {
      status: dbStatus === 'connected' ? 'ok' : 'degraded',
      db: dbStatus,
      uptime: Math.round(process.uptime() * 100) / 100,
      version: VERSION,
    };
  });

  // 5. Graceful shutdown — closes pg pool.
  app.addHook('onClose', async () => {
    if (pgClose) await pgClose();
  });

  return app;
};

// Entry point: only run when invoked directly (not when imported by tests).
const isMain = import.meta.url === `file://${process.argv[1]}`;
if (isMain) {
  const port = Number(process.env['PORT'] ?? 3000);
  buildCentralApp()
    .then((app) => app.listen({ port, host: '0.0.0.0' }))
    .then((addr) => {
      console.log(`central listening on ${addr}`);
    })
    .catch((err) => {
      console.error('central failed to start:', err);
      process.exit(1);
    });
}
