# Tasks: Fase 1 — Fundaciones Técnicas

> **Source of truth**: `DDL_V3.sql` (26 central tables + 13 local, flat naming). Zod schemas MUST match DDL column names, NOT the phase doc sketches. Greenfield — all files `Create`.

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | 2500–3200 |
| 800-line budget risk | **High** |
| Chained PRs recommended | **Yes** |
| Suggested split | PR1: tooling+schemas (~700L) → PR2: drizzle+migrations (~1200L) → PR3: seeders+CI+hardening (~800L) |
| Delivery strategy | single-pr |

Decision needed before apply: Yes
Chained PRs recommended: Yes
Chain strategy: pending
800-line budget risk: High

### Suggested Work Units

| Unit | Goal | Likely PR | Notes |
|------|------|-----------|-------|
| 1 | Monorepo tooling + all Zod schemas + tests | PR 1 | Base branch; standalone, verifiable with `pnpm test`, ~700L |
| 2 | Drizzle schemas + migrations + migrator + apps scaffolds | PR 2 | ~1200L; depends on PR1 for `shared-schemas` types |
| 3 | Docker + seeders + CI + hardening scripts | PR 3 | ~800L; depends on PR2 for DB + apps |

## Phase 1: Monorepo Tooling (~150L)
- [x] 1.1 Root configs: `package.json` (turbo scripts), `pnpm-workspace.yaml`, `turbo.json`, `.gitignore` — ~90L
- [x] 1.2 `tooling/tsconfig/{base,node}.json`: ES2022 strict, bundler resolution — ~35L
- [x] 1.3 `tooling/eslint-config/`: flat config extending typescript-eslint recommended — ~30L
- [x] 1.4 `tooling/prettier-config/`: semi, singleQuote, 100 cols — ~15L
- [x] 1.5 TEST: `pnpm install` resolves workspaces; `pnpm lint` green on empty repo

## Phase 2: Shared Zod Schemas (~550L)
- [x] 2.1 Init `packages/shared-schemas/`: `package.json` (zod dep), `tsconfig.json` — ~25L
- [x] 2.2 `src/core.ts`: User, Role, Province, Department, Locality, School, Classroom — DDL_V3 columns — ~80L
- [x] 2.3 `src/exam.ts`: Exam, ExamVersion, ExamBlock (5 types), ExamBlockOption, AnswerKey, ExamAsset, AssetUsage, Inputs — ~150L
- [x] 2.4 `src/sync.ts`: RegisteredNode, SyncCursor, SyncInbox, Pull/Push payloads, CentralDeliverySession, ReceivedAttempt, ReceivedAnswer, SyncAttempt — ~100L
- [x] 2.5 `src/audit.ts` + `src/settings.ts`: AuditLog (DDL: `ip_address`, `payload_json`), Settings — ~60L
- [x] 2.6 `src/index.ts`: barrel re-export all schemas + `z.infer<>` types — ~15L
- [x] 2.7 TDD: `__tests__/core.test.ts` — valid User, short email, invalid status enum, Province/Locality FK shape — ~60L
- [x] 2.8 TDD: `__tests__/exam.test.ts` — 5 block_types, empty options[], invalid type, ExamBlockOption required fields — ~70L
- [x] 2.9 TDD: `__tests__/sync.test.ts` + `__tests__/sync-protocol.test.ts` — empty PullResponse, multi-attempt Push, missing idempotencyKey, invalid direction enum — ~60L

## Phase 3: Drizzle ORM + Migrations (~900L hand-written + generated)
- [x] 3.1 Init `packages/shared-domain/`: `package.json` (drizzle-orm, pg, better-sqlite3), `tsconfig.json` — ~25L
- [x] 3.2 `src/db/central/schema.ts`: 26 tables from DDL_V3 (pg-core, `uuid().defaultRandom()`, CHECKs, indexes, triggers as raw SQL) — ~450L
- [x] 3.3 `src/db/central/relations.ts`: FK relations for type-safe Drizzle queries — ~90L
- [x] 3.4 `src/db/local/schema.ts`: 13 tables (sqlite-core, `text('id').primaryKey()`, TEXT dates) — ~220L
- [x] 3.5 `src/db/local/relations.ts`: FK relations — ~60L
- [x] 3.6 `src/db/migrator.ts`: `runMigrations(db, folder)` reusable — ~35L
- [x] 3.7 `apps/central/drizzle.config.ts` + `apps/local/drizzle.config.ts` — ~30L
- [x] 3.8 Generate: `drizzle-kit generate` for central + local → `src/db/migrations/` — auto SQL
- [x] 3.9 TDD: integration test — apply migrations on fresh PG container + SQLite `:memory:`, assert table counts (26 central, 13 local) — ~70L

## Phase 4: Apps Scaffolds (~200L)
- [x] 4.1 `apps/central/`: `package.json`, Fastify app + `GET /api/health` (status, db, uptime, version) + postgres plugin (decorates `fastify.db`) + `.env.example` — ~85L
- [x] 4.2 `apps/local/`: `package.json`, Fastify app + health + sqlite plugin (WAL, FK, busy_timeout PRAGMAs) + Electron stub + `.env.example` — ~110L
- [x] 4.3 TDD: smoke — curl `:3000/api/health` and `:3001/api/health`, assert 200 + JSON shape — ~30L

## Phase 5: Docker + Seeders (~420L)
- [ ] 5.1 `docker-compose.yml`: PG 16 alpine, pgcrypto extension, healthcheck `pg_isready`, named volume `pgdata` — ~25L
- [ ] 5.2 Geography seeder: 1 province → 3 depts → 6 localities → 6 schools → 12 classrooms (idempotent, DDL_V3 columns: `cue BIGINT`, `postal_code`, `shift`) — ~110L
- [ ] 5.3 Roles + users seeder: admin/operator/viewer roles; 3 users (bcrypt) with role assignments — ~80L
- [ ] 5.4 Exams seeder: 3 exams with all 5 block_types + exam_block_options + answer_keys (idempotent) — ~130L
- [ ] 5.5 Local seeder: 2 local_users + app_settings (central_url, jwt_secret, sync_interval, node_label) — ~50L
- [ ] 5.6 TDD: `__tests__/seeders.test.ts` — idempotency, row counts, Zod validation of seeded data, answer_key↔block cross-reference — ~80L

## Phase 6: CI + Hardening (~230L)
- [ ] 6.1 `.github/workflows/ci.yml`: checkout → pnpm setup → install frozen → lint → typecheck → build → migrate on PG service container — ~70L
- [ ] 6.2 `scripts/validate-schemas.ts`: Zod↔Drizzle field drift detection, fail-loud — ~55L
- [ ] 6.3 `scripts/reset-db.sh`: `docker compose down -v && up -d && pnpm db:migrate` — ~15L
- [ ] 6.4 `src/services/exam.service.ts`: `computeChecksum()` for version integrity — ~30L
- [ ] 6.5 `README.md`: quick start (clone → pnpm i → docker up → migrate → seed → curl health) — ~50L
- [ ] 6.6 Integration smoke: full clean install cycle, all tasks green, CI passes
