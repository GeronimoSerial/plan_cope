/**
 * Local SQLite schema (Drizzle ORM, sqlite-core).
 *
 * Source of truth: DDL_V3.sql — 13 tables, TEXT primary keys (UUIDs),
 * TEXT dates in ISO 8601. PRAGMAs (WAL, foreign_keys, busy_timeout) are
 * applied by the `apps/local` sqlite plugin; this file is the schema only.
 *
 * Note: Drizzle 0.36 sqlite-core's `extraConfig` returns an OBJECT
 * (`Record<string, IndexBuilder | CheckBuilder | ...>`), not an array.
 */
import { check, index, integer, sqliteTable, text } from 'drizzle-orm/sqlite-core';
import { sql } from 'drizzle-orm';

// ============================================================================
// LOCAL USERS
// ============================================================================

export const localUsers = sqliteTable('local_users', {
  id: text('id').primaryKey(),
  username: text('username').notNull().unique(),
  password_hash: text('password_hash').notNull(),
  role: text('role').notNull(),
  active: integer('active').notNull().default(1),
  last_login_at: text('last_login_at'),
  created_at: text('created_at').notNull(),
});

// ============================================================================
// LOCAL EXAMS
// ============================================================================

export const localExamVersions = sqliteTable(
  'local_exam_versions',
  {
    id: text('id').primaryKey(),
    remote_exam_version_id: text('remote_exam_version_id').notNull(),
    exam_code: text('exam_code').notNull(),
    version_number: integer('version_number').notNull(),
    checksum: text('checksum').notNull(),
    metadata_json: text('metadata_json'),
    schema_version: integer('schema_version').notNull(),
    synced_at: text('synced_at').notNull(),
  },
  (t) => ({
    remoteIdx: index('idx_local_exam_versions_remote').on(t.remote_exam_version_id),
  }),
);

export const localExamBlocks = sqliteTable(
  'local_exam_blocks',
  {
    id: text('id').primaryKey(),
    local_exam_version_id: text('local_exam_version_id')
      .notNull()
      .references(() => localExamVersions.id, { onDelete: 'cascade' }),
    remote_block_id: text('remote_block_id').notNull(),
    order_index: integer('order_index').notNull(),
    block_type: text('block_type').notNull(),
    config_json: text('config_json').notNull(),
    validation_json: text('validation_json'),
  },
  (t) => ({
    versionIdx: index('idx_local_exam_blocks_version_id').on(t.local_exam_version_id),
    blockTypeCheck: check(
      'CK_local_exam_blocks_block_type',
      sql`${t.block_type} IN ('text', 'image', 'multiple_choice', 'true_false', 'short_answer')`,
    ),
  }),
);

export const localAnswerKeys = sqliteTable(
  'local_answer_keys',
  {
    id: text('id').primaryKey(),
    local_exam_version_id: text('local_exam_version_id')
      .notNull()
      .references(() => localExamVersions.id, { onDelete: 'cascade' }),
    remote_block_id: text('remote_block_id').notNull(),
    correct_answer_json: text('correct_answer_json').notNull(),
    score_value: integer('score_value'),
  },
  (t) => ({
    versionIdx: index('idx_local_answer_keys_version_id').on(t.local_exam_version_id),
  }),
);

// ============================================================================
// LOCAL ASSETS
// ============================================================================

export const localAssets = sqliteTable(
  'local_assets',
  {
    id: text('id').primaryKey(),
    remote_asset_id: text('remote_asset_id').notNull(),
    local_exam_version_id: text('local_exam_version_id')
      .notNull()
      .references(() => localExamVersions.id, { onDelete: 'cascade' }),
    file_name: text('file_name').notNull(),
    mime_type: text('mime_type').notNull(),
    checksum: text('checksum').notNull(),
    local_path: text('local_path').notNull(),
    synced_at: text('synced_at').notNull(),
  },
  (t) => ({
    versionIdx: index('idx_local_assets_version_id').on(t.local_exam_version_id),
  }),
);

// ============================================================================
// DELIVERY SESSIONS
// ============================================================================

export const deliverySessions = sqliteTable('delivery_sessions', {
  id: text('id').primaryKey(),
  exam_version_id: text('exam_version_id').notNull(),
  school_code: text('school_code').notNull(),
  classroom_code: text('classroom_code'),
  commission_code: text('commission_code'),
  started_by: text('started_by').notNull(),
  start_at: text('start_at').notNull(),
  end_at: text('end_at'),
  status: text('status').notNull(),
  config_json: text('config_json'),
});

// ============================================================================
// STUDENT ATTEMPTS
// ============================================================================

export const studentAttempts = sqliteTable(
  'student_attempts',
  {
    id: text('id').primaryKey(),
    delivery_session_id: text('delivery_session_id')
      .notNull()
      .references(() => deliverySessions.id, { onDelete: 'cascade' }),
    student_code: text('student_code').notNull(),
    status: text('status').notNull(),
    started_at: text('started_at').notNull(),
    submitted_at: text('submitted_at'),
    local_sequence: integer('local_sequence').notNull(),
    confirmation_code: text('confirmation_code'),
  },
  (t) => ({
    deliveryIdx: index('idx_student_attempts_delivery_id').on(t.delivery_session_id),
    studentIdx: index('idx_student_attempts_student_code').on(t.student_code),
    statusCheck: check(
      'CK_student_attempts_status',
      sql`${t.status} IN ('pending', 'in_progress', 'submitted', 'graded')`,
    ),
  }),
);

export const submissionAnswers = sqliteTable(
  'submission_answers',
  {
    id: text('id').primaryKey(),
    student_attempt_id: text('student_attempt_id')
      .notNull()
      .references(() => studentAttempts.id, { onDelete: 'cascade' }),
    block_id: text('block_id')
      .notNull()
      .references(() => localExamBlocks.id),
    answer_json: text('answer_json').notNull(),
    created_at: text('created_at').notNull(),
  },
  (t) => ({
    attemptIdx: index('idx_submission_answers_attempt_id').on(t.student_attempt_id),
    blockIdx: index('idx_submission_answers_block_id').on(t.block_id),
  }),
);

// ============================================================================
// OUTBOX / SYNC STATE
// ============================================================================

export const syncOutbox = sqliteTable(
  'sync_outbox',
  {
    id: text('id').primaryKey(),
    event_type: text('event_type').notNull(),
    aggregate_type: text('aggregate_type').notNull(),
    aggregate_id: text('aggregate_id').notNull(),
    idempotency_key: text('idempotency_key').notNull().unique(),
    payload_json: text('payload_json').notNull(),
    status: text('status').notNull().default('pending'),
    retry_count: integer('retry_count').notNull().default(0),
    next_retry_at: text('next_retry_at'),
    last_error: text('last_error'),
    created_at: text('created_at').notNull(),
    processed_at: text('processed_at'),
  },
  (t) => ({
    pendingIdx: index('idx_sync_outbox_pending').on(t.status, t.retry_count, t.next_retry_at),
    aggregateIdx: index('idx_sync_outbox_aggregate').on(t.aggregate_type, t.aggregate_id),
    statusCheck: check(
      'CK_sync_outbox_status',
      sql`${t.status} IN ('pending', 'processing', 'completed', 'failed', 'discarded')`,
    ),
  }),
);

export const syncState = sqliteTable('sync_state', {
  id: text('id').primaryKey(),
  key: text('key').notNull().unique(),
  value_json: text('value_json').notNull(),
  updated_at: text('updated_at').notNull(),
});

export const syncAttempts = sqliteTable(
  'sync_attempts',
  {
    id: text('id').primaryKey(),
    direction: text('direction').notNull(),
    status: text('status').notNull(),
    started_at: text('started_at').notNull(),
    finished_at: text('finished_at'),
    summary_json: text('summary_json'),
    error_json: text('error_json'),
  },
  (t) => ({
    directionIdx: index('idx_sync_attempts_direction').on(t.direction, t.started_at),
    directionCheck: check(
      'CK_sync_attempts_direction',
      sql`${t.direction} IN ('inbound', 'outbound')`,
    ),
    statusCheck: check(
      'CK_sync_attempts_status',
      sql`${t.status} IN ('running', 'completed', 'failed')`,
    ),
  }),
);

// ============================================================================
// AUDIT
// ============================================================================

export const localAuditLogs = sqliteTable(
  'local_audit_logs',
  {
    id: text('id').primaryKey(),
    actor_id: text('actor_id'),
    action: text('action').notNull(),
    entity_type: text('entity_type').notNull(),
    entity_id: text('entity_id').notNull(),
    payload_json: text('payload_json'),
    created_at: text('created_at').notNull(),
  },
  (t) => ({
    entityIdx: index('idx_local_audit_logs_entity').on(t.entity_type, t.entity_id),
  }),
);

// ============================================================================
// APP SETTINGS
// ============================================================================

export const appSettings = sqliteTable('app_settings', {
  id: text('id').primaryKey(),
  key: text('key').notNull().unique(),
  value_json: text('value_json').notNull(),
  updated_at: text('updated_at').notNull(),
});
