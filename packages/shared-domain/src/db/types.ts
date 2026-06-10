/**
 * Type-only re-exports for shared-domain. We deliberately avoid
 * `export *` here to prevent the `syncAttempts` / `syncAttemptsRelations`
 * collision between central (PostgreSQL) and local (SQLite) schemas.
 */
import type * as centralSchema from './central/schema.js';
import type * as localSchema from './local/schema.js';

export type PgCentralSchema = typeof centralSchema;
export type SqliteLocalSchema = typeof localSchema;
