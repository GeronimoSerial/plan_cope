/**
 * Fastify module augmentation: decorates `fastify.db` with a Drizzle handle.
 * This file is referenced via `/// <reference path="..." />` from each route
 * OR simply imported once at app boot (the side-effect augments the module).
 */
import 'fastify';
import type { PostgresJsDatabase } from 'drizzle-orm/postgres-js';

declare module 'fastify' {
  interface FastifyInstance {
    db: PostgresJsDatabase;
  }
}

// Marker to keep the import above alive under type-stripping.
export type _Aug = PostgresJsDatabase;
