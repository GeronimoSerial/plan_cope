/**
 * Fastify module augmentation for apps/local: decorates `fastify.db` with a
 * Drizzle better-sqlite3 handle.
 */
import 'fastify';
import type { BetterSQLite3Database } from 'drizzle-orm/better-sqlite3';

declare module 'fastify' {
  interface FastifyInstance {
    db: BetterSQLite3Database;
  }
}

export type _Aug = BetterSQLite3Database;
