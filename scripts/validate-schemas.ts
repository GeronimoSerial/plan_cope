#!/usr/bin/env -S npx tsx
/**
 * validate-schemas.ts — Zod ↔ Drizzle drift detection.
 *
 * Walks every `*Insert` Zod schema in `@plan-cope/shared-schemas` and
 * the matching Drizzle column in `@plan-cope/shared-domain` and asserts
 * the two are in sync. We check:
 *
 *   1. Column presence: every Zod field must map to a real Drizzle
 *      column. Every Drizzle NOT-NULL column must have a Zod field
 *      (with two carve-outs: auto-managed timestamps and DB-defaulted
 *      fields, where Zod is allowed to mark the field optional).
 *   2. Nullability: a Zod field declared as nullable/optional must
 *      not back a Drizzle column declared `.notNull()` (unless the
 *      column also has a DB-side default that fills in the value).
 *   3. Type tags: a Zod field declared as a UUID must correspond to
 *      a Drizzle column tagged as `string` (UUID is text in Drizzle).
 *
 * The script exits non-zero on the first drift it finds — the whole
 * point is to fail the build, not to collect every discrepancy.
 *
 * Run with `pnpm validate:schemas` from the repo root.
 */
import { exit } from 'node:process';
import type { z } from 'zod';
import * as schemas from '../packages/shared-schemas/src/index.ts';
import * as central from '../packages/shared-domain/src/db/central/schema.ts';

interface Drift {
  table: string;
  field: string;
  issue: string;
}

const drifts: Drift[] = [];

// Timestamps that are auto-managed by the DB (default NOW() or
// application-managed). These do NOT need a matching Zod field.
const AUTO_MANAGED_FIELDS = new Set([
  'id',
  'created_at',
  'updated_at',
  'published_at',
  'received_at',
  'started_at',
  'submitted_at',
  'last_login_at',
  'last_seen_at',
  'ended_at',
  'synced_at',
  'processed_at',
  'deleted_at',
]);

interface DrizzleColumn {
  name: string;
  notNull: boolean;
  dataType: string;
  hasDefault: boolean;
}

const isDrizzleColumn = (v: unknown): v is DrizzleColumn => {
  if (!v || typeof v !== 'object') return false;
  const c = v as Record<string, unknown>;
  return (
    typeof c['name'] === 'string' &&
    typeof c['notNull'] === 'boolean' &&
    typeof c['dataType'] === 'string'
  );
};

/** Detect whether a Zod field is a UUID string. */
const isUuidZod = (schema: z.ZodTypeAny): boolean => {
  if (!schema || !('_def' in schema)) return false;
  const def = schema._def as { typeName?: string; checks?: Array<{ kind: string }> };
  if (def.typeName !== 'ZodString') return false;
  return (def.checks ?? []).some((c) => c.kind === 'uuid');
};

/** Detect whether a Zod field is "optional" (i.e. nullable/optional). */
const isOptionalZod = (schema: z.ZodTypeAny): boolean => {
  if (!schema || !('_def' in schema)) return false;
  const def = schema._def as { typeName?: string; innerType?: z.ZodTypeAny };
  if (def.typeName === 'ZodOptional' || def.typeName === 'ZodNullable') return true;
  if (def.innerType) return isOptionalZod(def.innerType);
  return false;
};

const fail = (d: Drift): void => {
  drifts.push(d);
  console.error(`drift: ${d.table}.${d.field} → ${d.issue}`);
};

const checkTable = (
  tableName: string,
  tableObj: unknown,
  zodSchema: z.ZodObject<z.ZodRawShape>,
): void => {
  // Collect columns. A Drizzle column has a string `name`, a boolean
  // `notNull`, a string `dataType`, and a `hasDefault` flag we can
  // introspect.
  const columns = new Map<string, DrizzleColumn>();
  for (const [key, value] of Object.entries(tableObj as Record<string, unknown>)) {
    if (isDrizzleColumn(value)) {
      columns.set(key, value);
    }
  }

  // Forward: every Zod field must map to a real Drizzle column.
  const zodShape = zodSchema.shape;
  for (const [zodKey, zodFieldUntyped] of Object.entries(zodShape)) {
    const zodField = zodFieldUntyped as z.ZodTypeAny;
    const matchingColumn = columns.get(zodKey);
    if (!matchingColumn) {
      fail({
        table: tableName,
        field: zodKey,
        issue: 'Zod field has no matching Drizzle column',
      });
      continue;
    }
    if (matchingColumn.notNull && isOptionalZod(zodField) && !matchingColumn.hasDefault) {
      fail({
        table: tableName,
        field: zodKey,
        issue: 'Drizzle column is NOT NULL without default but Zod field is optional/nullable',
      });
    }
    if (isUuidZod(zodField) && matchingColumn.dataType !== 'string') {
      fail({
        table: tableName,
        field: zodKey,
        issue: `Zod field is uuid but Drizzle column type is "${matchingColumn.dataType}"`,
      });
    }
  }

  // Reverse: every Drizzle NOT-NULL column must have a Zod field.
  for (const [colKey, col] of columns) {
    if (!col.notNull) continue;
    // Skip auto-managed timestamps / PKs.
    if (AUTO_MANAGED_FIELDS.has(col.name)) continue;
    if (!(colKey in zodShape)) {
      fail({
        table: tableName,
        field: colKey,
        issue: 'Drizzle NOT-NULL column has no Zod field',
      });
    }
  }
};

// Map Zod `*Insert` schemas to Drizzle tables. Static allow-list — we
// want a fail-loud drift detector, not clever inference.
const MAPPING: ReadonlyArray<{
  table: string;
  zod: z.ZodObject<z.ZodRawShape>;
  drizzle: unknown;
}> = [
  // Core
  { table: 'users', zod: schemas.userInsert, drizzle: central.users },
  { table: 'roles', zod: schemas.roleInsert, drizzle: central.roles },
  { table: 'user_roles', zod: schemas.userRoleInsert, drizzle: central.userRoles },
  { table: 'provinces', zod: schemas.provinceInsert, drizzle: central.provinces },
  { table: 'departments', zod: schemas.departmentInsert, drizzle: central.departments },
  { table: 'localities', zod: schemas.localityInsert, drizzle: central.localities },
  { table: 'schools', zod: schemas.schoolInsert, drizzle: central.schools },
  { table: 'classrooms', zod: schemas.classroomInsert, drizzle: central.classrooms },
  // Exams
  { table: 'exams', zod: schemas.examInsert, drizzle: central.exams },
  { table: 'exam_versions', zod: schemas.examVersionInsert, drizzle: central.examVersions },
  { table: 'exam_block_options', zod: schemas.examBlockOptionInsert, drizzle: central.examBlockOptions },
  { table: 'answer_keys', zod: schemas.answerKeyInsert, drizzle: central.answerKeys },
  { table: 'exam_assets', zod: schemas.examAssetInsert, drizzle: central.examAssets },
  { table: 'asset_usages', zod: schemas.assetUsageInsert, drizzle: central.assetUsages },
  // Sync
  { table: 'registered_nodes', zod: schemas.registeredNodeInsert, drizzle: central.registeredNodes },
  { table: 'central_delivery_sessions', zod: schemas.centralDeliverySessionInsert, drizzle: central.centralDeliverySessions },
  { table: 'received_student_attempts', zod: schemas.receivedStudentAttemptInsert, drizzle: central.receivedStudentAttempts },
  { table: 'received_submission_answers', zod: schemas.receivedSubmissionAnswerInsert, drizzle: central.receivedSubmissionAnswers },
  { table: 'sync_inbox', zod: schemas.syncInboxInsert, drizzle: central.syncInbox },
  { table: 'sync_cursors', zod: schemas.syncCursorInsert, drizzle: central.syncCursors },
  { table: 'sync_attempts', zod: schemas.syncAttemptInsert, drizzle: central.syncAttempts },
  // Audit / settings
  { table: 'audit_logs', zod: schemas.auditLogInsert, drizzle: central.auditLogs },
  { table: 'settings', zod: schemas.settingInsert, drizzle: central.settings },
];

console.log(`validate-schemas: checking ${MAPPING.length} table↔zod pairs…`);

for (const m of MAPPING) {
  checkTable(m.table, m.drizzle, m.zod);
}

if (drifts.length > 0) {
  console.error(`\nvalidate-schemas: ${drifts.length} drift(s) found. Failing.`);
  exit(1);
}

console.log('validate-schemas: OK — Zod ↔ Drizzle schemas are in sync.');
