// Database-agnostic tables barrel. Schema-only; tables are imported directly
// from the dialect-specific sub-paths to avoid naming collisions:
//
//   import { users } from '@plan-cope/shared-domain/db/central/schema';
//   import { localUsers } from '@plan-cope/shared-domain/db/local/schema';
//
// Re-exporting both here with `export *` collides on `syncAttempts` and
// `syncAttemptsRelations`. Apps that need the full set use the per-dialect
// imports above.
export type { PgCentralSchema, SqliteLocalSchema } from './types.js';
