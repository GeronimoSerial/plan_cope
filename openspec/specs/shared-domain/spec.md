# shared-domain Specification

## Purpose
Capa de acceso a datos con Drizzle ORM. Define modelos para PostgreSQL (central, 26 tablas, flat naming según DDL_V3) y SQLite (local, 13 tablas con WAL mode). Incluye migraciones SQL autogeneradas y seeders de datos iniciales.

## Requirements

| # | Requirement | Strength |
|---|-------------|----------|
| R1 | Drizzle schema definitions for 26 central tables (flat naming, per DDL_V3) | MUST |
| R2 | Drizzle schema definitions for 13 local tables with SQLite TEXT UUIDs | MUST |
| R3 | Drizzle relations (foreign keys, cascade deletes, unique constraints) | MUST |
| R4 | SQL migration generation via `drizzle-kit generate` | MUST |
| R5 | Auto-apply migrations on app startup | MUST |
| R6 | Seeders for geography (1 province, 6 schools), roles (admin/operator/viewer), 3 users, 3 exams | SHOULD |
| R7 | Migration rollback safety: verify DB state before applying | SHOULD |

### Requirement: Central PostgreSQL Drizzle Schema

The `shared-domain` package SHALL define 26 Drizzle ORM tables with flat naming (per DDL_V3). All UUID columns MUST use `uuid('id').defaultRandom().primaryKey()`. Timestamp columns MUST use `timestamp('created_at').defaultNow()`. Soft-delete columns (`deleted_at`) MUST allow null. Check constraints for status enums MUST match the DDL.

#### Scenario: All 20 tables generate without errors

- GIVEN `drizzle-kit generate` is executed against `db/central/schema.ts`
- WHEN the migration SQL is produced
- THEN all 26 CREATE TABLE statements exist AND no errors are reported

#### Scenario: UUID primary key auto-generates

- GIVEN a new user row is inserted without explicit `id`
- WHEN Drizzle executes the insert
- THEN the row receives a valid UUID v4 `id`

#### Scenario: Foreign key cascade works

- GIVEN a province with 3 departments
- WHEN the province is deleted
- THEN all 3 departments are also deleted (ON DELETE CASCADE)

### Requirement: Local SQLite Drizzle Schema

The package SHALL define 13 Drizzle ORM tables for SQLite with TEXT primary keys (UUIDs stored as text). Connection PRAGMAs MUST include `journal_mode=WAL`, `foreign_keys=ON`, `busy_timeout=5000`. Date fields MUST use ISO 8601 TEXT format.

#### Scenario: SQLite WAL mode is activated

- GIVEN the local app starts
- WHEN `PRAGMA journal_mode=WAL` is executed
- THEN the database returns `wal` as journal mode

#### Scenario: Foreign keys are enforced in SQLite

- GIVEN a delivery_session with student_attempts
- WHEN the delivery_session is deleted
- THEN all related student_attempts are deleted (ON DELETE CASCADE)

#### Scenario: Schema migration creates all 13 tables

- GIVEN a fresh SQLite database
- WHEN `drizzle-kit migrate` is executed with local migrations
- THEN 13 tables exist with correct columns and indexes

### Requirement: Migration Auto-Apply at Startup

Both `apps/central` and `apps/local` SHALL execute pending migrations automatically on startup. If a migration fails, the app MUST log the error and MUST NOT start the server. Successfully applied migrations SHALL be tracked in the `__drizzle_migrations` table.

#### Scenario: Fresh startup applies all migrations

- GIVEN an empty database
- WHEN the app starts
- THEN all pending migration SQL files are executed AND the app starts serving

#### Scenario: No pending migrations skips execution

- GIVEN all migrations already applied
- WHEN the app starts
- THEN no SQL is executed AND the app starts immediately

#### Scenario: Failed migration prevents startup

- GIVEN a migration file with invalid SQL
- WHEN the app attempts to apply it
- THEN an error is logged to stderr AND the server does NOT start

### Requirement: Seeders Populate Initial Data

Seeders SHALL be idempotent — checking for existing data before inserting. Geography seeder MUST create 1 province, 3 departments, 6 localities, 6 schools, 12 classrooms. User seeder MUST create 3 roles (admin, operator, viewer) and 3 users. Exam seeder MUST create 3 complete exams with blocks spanning all 5 types and corresponding answer keys.

#### Scenario: Geography seeder creates full hierarchy

- GIVEN empty core tables
- WHEN `pnpm --filter @plan-cope/central db:seed` runs
- THEN 1 province, 3 departments, 6 localities, 6 schools, 12 classrooms exist

#### Scenario: Seeder is idempotent on re-run

- GIVEN data already exists in all tables
- WHEN the seeder runs again
- THEN no duplicate rows are inserted AND no errors occur

#### Scenario: Exam seeder with answer keys validates

- GIVEN 3 exams are seeded
- WHEN each exam block is cross-referenced with its answer key
- THEN every block with `required: true` has a matching AnswerKey record
