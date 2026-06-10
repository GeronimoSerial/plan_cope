/**
 * Central PostgreSQL schema (Drizzle ORM, pg-core).
 *
 * Source of truth: DDL_V3.sql — 26 tables, all in `public` schema, snake_case
 * column names, no PG schema prefix. Drizzle table *exports* use camelCase per
 * Drizzle convention; the table NAME argument and all column `.text()` / `.uuid()`
 * arguments stay snake_case to match the DDL exactly.
 */
import {
  bigint,
  check,
  index,
  integer,
  jsonb,
  numeric,
  pgTable,
  primaryKey,
  text,
  timestamp,
  uniqueIndex,
  uuid,
} from 'drizzle-orm/pg-core';
import { sql } from 'drizzle-orm';

// ============================================================================
// USERS / ROLES
// ============================================================================

export const users = pgTable(
  'users',
  {
    id: uuid('id').defaultRandom().primaryKey(),
    email: text('email').notNull(),
    password_hash: text('password_hash').notNull(),
    full_name: text('full_name').notNull(),
    status: text('status').notNull().default('active'),
    last_login_at: timestamp('last_login_at', { withTimezone: true }),
    deleted_at: timestamp('deleted_at', { withTimezone: true }),
    created_at: timestamp('created_at', { withTimezone: true })
      .notNull()
      .defaultNow(),
    updated_at: timestamp('updated_at', { withTimezone: true })
      .notNull()
      .defaultNow(),
  },
  (t) => [
    uniqueIndex('UQ_users_email').on(t.email),
    check('CK_users_status', sql`${t.status} IN ('active', 'inactive', 'suspended')`),
    // Mirrors DDL: idx_users_email_active WHERE deleted_at IS NULL AND status = 'active'
    index('idx_users_email_active')
      .on(t.email)
      .where(sql`${t.deleted_at} IS NULL AND ${t.status} = 'active'`),
  ],
);

export const roles = pgTable(
  'roles',
  {
    id: uuid('id').defaultRandom().primaryKey(),
    code: text('code').notNull(),
    name: text('name').notNull(),
    description: text('description'),
    created_at: timestamp('created_at', { withTimezone: true })
      .notNull()
      .defaultNow(),
  },
  (t) => [uniqueIndex('UQ_roles_code').on(t.code)],
);

export const userRoles = pgTable(
  'user_roles',
  {
    user_id: uuid('user_id')
      .notNull()
      .references(() => users.id, { onDelete: 'cascade' }),
    role_id: uuid('role_id')
      .notNull()
      .references(() => roles.id, { onDelete: 'cascade' }),
    created_at: timestamp('created_at', { withTimezone: true })
      .notNull()
      .defaultNow(),
  },
  (t) => [primaryKey({ columns: [t.user_id, t.role_id] })],
);

// ============================================================================
// SCHOOLS / ORGANIZATION
// ============================================================================

export const provinces = pgTable(
  'provinces',
  {
    id: uuid('id').defaultRandom().primaryKey(),
    code: text('code').notNull(),
    name: text('name').notNull(),
    created_at: timestamp('created_at', { withTimezone: true })
      .notNull()
      .defaultNow(),
  },
  (t) => [uniqueIndex('UQ_provinces_code').on(t.code)],
);

export const departments = pgTable(
  'departments',
  {
    id: uuid('id').defaultRandom().primaryKey(),
    code: text('code').notNull(),
    name: text('name').notNull(),
    province_id: uuid('province_id')
      .notNull()
      .references(() => provinces.id, { onDelete: 'cascade' }),
    created_at: timestamp('created_at', { withTimezone: true })
      .notNull()
      .defaultNow(),
  },
  (t) => [
    uniqueIndex('UQ_departments_province_code').on(t.province_id, t.code),
  ],
);

export const localities = pgTable(
  'localities',
  {
    id: uuid('id').defaultRandom().primaryKey(),
    department_id: uuid('department_id')
      .notNull()
      .references(() => departments.id, { onDelete: 'cascade' }),
    code: text('code').notNull(),
    postal_code: text('postal_code'),
    name: text('name').notNull(),
    created_at: timestamp('created_at', { withTimezone: true })
      .notNull()
      .defaultNow(),
  },
  (t) => [
    uniqueIndex('UQ_localities_department_code').on(t.department_id, t.code),
  ],
);

export const schools = pgTable(
  'schools',
  {
    id: uuid('id').defaultRandom().primaryKey(),
    code: text('code').notNull(),
    cue: bigint('cue', { mode: 'bigint' }).notNull(),
    annex: integer('annex'),
    name: text('name').notNull(),
    locality_id: uuid('locality_id')
      .notNull()
      .references(() => localities.id, { onDelete: 'cascade' }),
    status: text('status').notNull().default('active'),
    deleted_at: timestamp('deleted_at', { withTimezone: true }),
    created_at: timestamp('created_at', { withTimezone: true })
      .notNull()
      .defaultNow(),
    updated_at: timestamp('updated_at', { withTimezone: true })
      .notNull()
      .defaultNow(),
  },
  (t) => [
    uniqueIndex('UQ_schools_locality_code').on(t.locality_id, t.code),
    check(
      'CK_schools_status',
      sql`${t.status} IN ('active', 'inactive', 'closed')`,
    ),
    index('idx_schools_status_active')
      .on(t.status)
      .where(sql`${t.deleted_at} IS NULL AND ${t.status} = 'active'`),
  ],
);

export const classrooms = pgTable(
  'classrooms',
  {
    id: uuid('id').defaultRandom().primaryKey(),
    school_id: uuid('school_id')
      .notNull()
      .references(() => schools.id, { onDelete: 'cascade' }),
    code: text('code').notNull(),
    name: text('name').notNull(),
    shift: text('shift'),
    metadata_json: jsonb('metadata_json'),
    deleted_at: timestamp('deleted_at', { withTimezone: true }),
    created_at: timestamp('created_at', { withTimezone: true })
      .notNull()
      .defaultNow(),
    updated_at: timestamp('updated_at', { withTimezone: true })
      .notNull()
      .defaultNow(),
  },
  (t) => [
    check(
      'CK_classrooms_shift',
      sql`${t.shift} IS NULL OR ${t.shift} IN ('morning', 'afternoon', 'night', 'full_day')`,
    ),
    index('idx_classrooms_school_active')
      .on(t.school_id)
      .where(sql`${t.deleted_at} IS NULL`),
  ],
);

// ============================================================================
// EXAMS
// ============================================================================

export const exams = pgTable(
  'exams',
  {
    id: uuid('id').defaultRandom().primaryKey(),
    code: text('code').notNull(),
    title: text('title').notNull(),
    description: text('description'),
    level: text('level'),
    area: text('area'),
    subject: text('subject'),
    status: text('status').notNull().default('active'),
    deleted_at: timestamp('deleted_at', { withTimezone: true }),
    created_at: timestamp('created_at', { withTimezone: true })
      .notNull()
      .defaultNow(),
    updated_at: timestamp('updated_at', { withTimezone: true })
      .notNull()
      .defaultNow(),
  },
  (t) => [
    uniqueIndex('UQ_exams_code').on(t.code),
    check(
      'CK_exams_status',
      sql`${t.status} IN ('active', 'inactive', 'archived')`,
    ),
    index('idx_exams_status_active')
      .on(t.status)
      .where(sql`${t.deleted_at} IS NULL AND ${t.status} = 'active'`),
  ],
);

export const examVersions = pgTable(
  'exam_versions',
  {
    id: uuid('id').defaultRandom().primaryKey(),
    exam_id: uuid('exam_id')
      .notNull()
      .references(() => exams.id, { onDelete: 'cascade' }),
    version_number: integer('version_number').notNull(),
    schema_version: integer('schema_version').notNull().default(1),
    status: text('status').notNull().default('draft'),
    metadata_json: jsonb('metadata_json'),
    created_by: uuid('created_by').references(() => users.id),
    reviewed_by: uuid('reviewed_by').references(() => users.id),
    approved_by: uuid('approved_by').references(() => users.id),
    published_by: uuid('published_by').references(() => users.id),
    published_at: timestamp('published_at', { withTimezone: true }),
    created_at: timestamp('created_at', { withTimezone: true })
      .notNull()
      .defaultNow(),
    updated_at: timestamp('updated_at', { withTimezone: true })
      .notNull()
      .defaultNow(),
  },
  (t) => [
    uniqueIndex('UQ_exam_versions_exam_version').on(t.exam_id, t.version_number),
    check(
      'CK_exam_versions_status',
      sql`${t.status} IN ('draft', 'review', 'approved', 'published', 'archived')`,
    ),
    check('CK_exam_versions_version_positive', sql`${t.version_number} > 0`),
    index('idx_exam_versions_exam_id').on(t.exam_id),
  ],
);

export const examBlocks = pgTable(
  'exam_blocks',
  {
    id: uuid('id').defaultRandom().primaryKey(),
    exam_version_id: uuid('exam_version_id')
      .notNull()
      .references(() => examVersions.id, { onDelete: 'cascade' }),
    order_index: integer('order_index').notNull(),
    block_type: text('block_type').notNull(),
    title: text('title'),
    description: text('description'),
    config_json: jsonb('config_json').notNull(),
    validation_json: jsonb('validation_json'),
    created_at: timestamp('created_at', { withTimezone: true })
      .notNull()
      .defaultNow(),
    updated_at: timestamp('updated_at', { withTimezone: true })
      .notNull()
      .defaultNow(),
  },
  (t) => [
    check(
      'CK_exam_blocks_block_type',
      sql`${t.block_type} IN ('text', 'image', 'multiple_choice', 'true_false', 'short_answer')`,
    ),
    index('idx_exam_blocks_exam_version_id').on(t.exam_version_id),
    // DDL: CREATE INDEX idx_exam_blocks_config_json ON exam_blocks USING GIN (config_json);
    index('idx_exam_blocks_config_json').using('gin', t.config_json),
  ],
);

export const examBlockOptions = pgTable(
  'exam_block_options',
  {
    id: uuid('id').defaultRandom().primaryKey(),
    exam_block_id: uuid('exam_block_id')
      .notNull()
      .references(() => examBlocks.id, { onDelete: 'cascade' }),
    value: text('value').notNull(),
    label: text('label').notNull(),
    order_index: integer('order_index').notNull().default(0),
    metadata_json: jsonb('metadata_json'),
    created_at: timestamp('created_at', { withTimezone: true })
      .notNull()
      .defaultNow(),
    updated_at: timestamp('updated_at', { withTimezone: true })
      .notNull()
      .defaultNow(),
  },
  (t) => [
    check('CK_exam_block_options_order', sql`${t.order_index} >= 0`),
    index('idx_exam_block_options_block_id').on(t.exam_block_id),
  ],
);

export const answerKeys = pgTable(
  'answer_keys',
  {
    id: uuid('id').defaultRandom().primaryKey(),
    exam_block_id: uuid('exam_block_id')
      .notNull()
      .references(() => examBlocks.id, { onDelete: 'cascade' }),
    correct_answer_json: jsonb('correct_answer_json').notNull(),
    score_value: numeric('score_value', { precision: 10, scale: 2 }),
    metadata_json: jsonb('metadata_json'),
    created_at: timestamp('created_at', { withTimezone: true })
      .notNull()
      .defaultNow(),
    updated_at: timestamp('updated_at', { withTimezone: true })
      .notNull()
      .defaultNow(),
  },
  (t) => [index('idx_answer_keys_exam_block_id').on(t.exam_block_id)],
);

// ============================================================================
// ASSETS
// ============================================================================

export const examAssets = pgTable(
  'exam_assets',
  {
    id: uuid('id').defaultRandom().primaryKey(),
    exam_version_id: uuid('exam_version_id')
      .notNull()
      .references(() => examVersions.id, { onDelete: 'cascade' }),
    file_name: text('file_name').notNull(),
    mime_type: text('mime_type').notNull(),
    size_bytes: bigint('size_bytes', { mode: 'bigint' }).notNull(),
    checksum: text('checksum').notNull(),
    storage_path: text('storage_path').notNull(),
    created_at: timestamp('created_at', { withTimezone: true })
      .notNull()
      .defaultNow(),
  },
  (t) => [
    check('CK_exam_assets_size_positive', sql`${t.size_bytes} > 0`),
    index('idx_exam_assets_exam_version_id').on(t.exam_version_id),
    uniqueIndex('UQ_exam_assets_version_checksum').on(
      t.exam_version_id,
      t.checksum,
    ),
  ],
);

export const assetUsages = pgTable(
  'asset_usages',
  {
    id: uuid('id').defaultRandom().primaryKey(),
    exam_block_id: uuid('exam_block_id')
      .notNull()
      .references(() => examBlocks.id, { onDelete: 'cascade' }),
    exam_asset_id: uuid('exam_asset_id')
      .notNull()
      .references(() => examAssets.id, { onDelete: 'cascade' }),
    usage_type: text('usage_type').notNull(),
    created_at: timestamp('created_at', { withTimezone: true })
      .notNull()
      .defaultNow(),
    updated_at: timestamp('updated_at', { withTimezone: true })
      .notNull()
      .defaultNow(),
  },
  (t) => [
    check(
      'CK_asset_usages_usage_type',
      sql`${t.usage_type} IN ('prompt', 'option_image', 'reference', 'attachment')`,
    ),
    index('idx_asset_usages_exam_block_id').on(t.exam_block_id),
    index('idx_asset_usages_exam_asset_id').on(t.exam_asset_id),
  ],
);

// ============================================================================
// PUBLICATION
// ============================================================================

export const publicationPackages = pgTable(
  'publication_packages',
  {
    id: uuid('id').defaultRandom().primaryKey(),
    exam_version_id: uuid('exam_version_id')
      .notNull()
      .references(() => examVersions.id, { onDelete: 'cascade' }),
    package_version: integer('package_version').notNull(),
    checksum: text('checksum').notNull(),
    manifest_json: jsonb('manifest_json').notNull(),
    status: text('status').notNull().default('published'),
    created_at: timestamp('created_at', { withTimezone: true })
      .notNull()
      .defaultNow(),
    published_at: timestamp('published_at', { withTimezone: true }),
  },
  (t) => [
    check(
      'CK_publication_packages_status',
      sql`${t.status} IN ('draft', 'published', 'revoked')`,
    ),
    check(
      'CK_publication_packages_version_positive',
      sql`${t.package_version} > 0`,
    ),
    index('idx_publication_packages_exam_version_id').on(t.exam_version_id),
    uniqueIndex('UQ_publication_packages_version').on(
      t.exam_version_id,
      t.package_version,
    ),
    // DDL: CREATE INDEX idx_publication_packages_published ... WHERE status = 'published'
    index('idx_publication_packages_published')
      .on(t.exam_version_id, t.status)
      .where(sql`${t.status} = 'published'`),
  ],
);

export const publicationTargets = pgTable(
  'publication_targets',
  {
    id: uuid('id').defaultRandom().primaryKey(),
    publication_package_id: uuid('publication_package_id')
      .notNull()
      .references(() => publicationPackages.id, { onDelete: 'cascade' }),
    target_type: text('target_type').notNull(),
    target_id: text('target_id').notNull(),
    created_at: timestamp('created_at', { withTimezone: true })
      .notNull()
      .defaultNow(),
    updated_at: timestamp('updated_at', { withTimezone: true })
      .notNull()
      .defaultNow(),
  },
  (t) => [
    check(
      'CK_publication_targets_type',
      sql`${t.target_type} IN ('school', 'department', 'province', 'node', 'global')`,
    ),
    index('idx_publication_targets_package_id').on(t.publication_package_id),
  ],
);

// ============================================================================
// REGISTERED NODES
// ============================================================================

export const registeredNodes = pgTable(
  'registered_nodes',
  {
    id: uuid('id').defaultRandom().primaryKey(),
    school_id: uuid('school_id').references(() => schools.id),
    node_code: text('node_code').notNull(),
    device_name: text('device_name'),
    status: text('status').notNull().default('active'),
    last_seen_at: timestamp('last_seen_at', { withTimezone: true }),
    created_at: timestamp('created_at', { withTimezone: true })
      .notNull()
      .defaultNow(),
    updated_at: timestamp('updated_at', { withTimezone: true })
      .notNull()
      .defaultNow(),
  },
  (t) => [
    uniqueIndex('UQ_registered_nodes_node_code').on(t.node_code),
    check(
      'CK_registered_nodes_status',
      sql`${t.status} IN ('active', 'inactive', 'blocked')`,
    ),
    index('idx_registered_nodes_school_id').on(t.school_id),
  ],
);

// ============================================================================
// CENTRAL DELIVERY SESSIONS
// ============================================================================

export const centralDeliverySessions = pgTable(
  'central_delivery_sessions',
  {
    id: uuid('id').defaultRandom().primaryKey(),
    remote_local_id: text('remote_local_id').notNull(),
    school_id: uuid('school_id').references(() => schools.id),
    exam_version_id: uuid('exam_version_id').references(() => examVersions.id),
    classroom_code: text('classroom_code'),
    commission_code: text('commission_code'),
    status: text('status').notNull(),
    started_at: timestamp('started_at', { withTimezone: true }),
    ended_at: timestamp('ended_at', { withTimezone: true }),
    synced_at: timestamp('synced_at', { withTimezone: true }),
    created_at: timestamp('created_at', { withTimezone: true })
      .notNull()
      .defaultNow(),
  },
  (t) => [
    check(
      'CK_central_delivery_sessions_status',
      sql`${t.status} IN ('pending', 'active', 'completed', 'cancelled')`,
    ),
    index('idx_central_delivery_school_id').on(t.school_id),
    index('idx_central_delivery_exam_version_id').on(t.exam_version_id),
    index('idx_central_delivery_school_active')
      .on(t.school_id, t.status)
      .where(sql`${t.status} = 'active'`),
  ],
);

// ============================================================================
// RECEIVED RESULTS
// ============================================================================

export const receivedStudentAttempts = pgTable(
  'received_student_attempts',
  {
    id: uuid('id').defaultRandom().primaryKey(),
    remote_local_id: text('remote_local_id').notNull(),
    delivery_session_id: uuid('delivery_session_id').references(
      () => centralDeliverySessions.id,
      { onDelete: 'set null' },
    ),
    student_code: text('student_code').notNull(),
    status: text('status').notNull(),
    started_at: timestamp('started_at', { withTimezone: true }),
    submitted_at: timestamp('submitted_at', { withTimezone: true }),
    received_at: timestamp('received_at', { withTimezone: true })
      .notNull()
      .defaultNow(),
    idempotency_key: text('idempotency_key').notNull(),
    created_at: timestamp('created_at', { withTimezone: true })
      .notNull()
      .defaultNow(),
  },
  (t) => [
    uniqueIndex('UQ_received_student_attempts_idempotency').on(t.idempotency_key),
    check(
      'CK_received_student_attempts_status',
      sql`${t.status} IN ('pending', 'in_progress', 'submitted', 'graded')`,
    ),
    index('idx_received_attempts_delivery_id').on(t.delivery_session_id),
    index('idx_received_attempts_student_code').on(t.student_code),
  ],
);

export const receivedSubmissionAnswers = pgTable(
  'received_submission_answers',
  {
    id: uuid('id').defaultRandom().primaryKey(),
    student_attempt_id: uuid('student_attempt_id')
      .notNull()
      .references(() => receivedStudentAttempts.id, { onDelete: 'cascade' }),
    block_id: uuid('block_id')
      .notNull()
      .references(() => examBlocks.id),
    answer_json: jsonb('answer_json').notNull(),
    created_at: timestamp('created_at', { withTimezone: true })
      .notNull()
      .defaultNow(),
  },
  (t) => [
    index('idx_received_answers_attempt_id').on(t.student_attempt_id),
    index('idx_received_answers_block_id').on(t.block_id),
  ],
);

// ============================================================================
// SYNC
// ============================================================================

export const syncInbox = pgTable(
  'sync_inbox',
  {
    id: uuid('id').defaultRandom().primaryKey(),
    source_node_id: uuid('source_node_id').references(() => registeredNodes.id, {
      onDelete: 'set null',
    }),
    event_type: text('event_type').notNull(),
    aggregate_type: text('aggregate_type').notNull(),
    aggregate_id: text('aggregate_id').notNull(),
    idempotency_key: text('idempotency_key').notNull(),
    payload_json: jsonb('payload_json').notNull(),
    status: text('status').notNull().default('pending'),
    created_at: timestamp('created_at', { withTimezone: true })
      .notNull()
      .defaultNow(),
    processed_at: timestamp('processed_at', { withTimezone: true }),
  },
  (t) => [
    uniqueIndex('UQ_sync_inbox_idempotency').on(t.idempotency_key),
    check(
      'CK_sync_inbox_status',
      sql`${t.status} IN ('pending', 'processing', 'completed', 'failed', 'discarded')`,
    ),
    index('idx_sync_inbox_source_node_id').on(t.source_node_id),
    // DDL: idx_sync_inbox_pending WHERE status = 'pending'
    index('idx_sync_inbox_pending')
      .on(t.status, t.created_at)
      .where(sql`${t.status} = 'pending'`),
    index('idx_sync_inbox_aggregate').on(t.aggregate_type, t.aggregate_id),
  ],
);

export const syncCursors = pgTable(
  'sync_cursors',
  {
    id: uuid('id').defaultRandom().primaryKey(),
    node_id: uuid('node_id')
      .notNull()
      .references(() => registeredNodes.id, { onDelete: 'cascade' }),
    cursor_key: text('cursor_key').notNull(),
    cursor_value: text('cursor_value').notNull(),
    updated_at: timestamp('updated_at', { withTimezone: true })
      .notNull()
      .defaultNow(),
  },
  (t) => [
    uniqueIndex('UQ_sync_cursors_node_cursor').on(t.node_id, t.cursor_key),
    index('idx_sync_cursors_node_id').on(t.node_id),
  ],
);

export const syncAttempts = pgTable(
  'sync_attempts',
  {
    id: uuid('id').defaultRandom().primaryKey(),
    node_id: uuid('node_id').references(() => registeredNodes.id),
    direction: text('direction').notNull(),
    status: text('status').notNull(),
    started_at: timestamp('started_at', { withTimezone: true })
      .notNull()
      .defaultNow(),
    finished_at: timestamp('finished_at', { withTimezone: true }),
    summary_json: jsonb('summary_json'),
    error_json: jsonb('error_json'),
  },
  (t) => [
    check(
      'CK_sync_attempts_direction',
      sql`${t.direction} IN ('inbound', 'outbound')`,
    ),
    check(
      'CK_sync_attempts_status',
      sql`${t.status} IN ('running', 'completed', 'failed')`,
    ),
    index('idx_sync_attempts_node_id').on(t.node_id),
  ],
);

// ============================================================================
// AUDIT
// ============================================================================

export const auditLogs = pgTable(
  'audit_logs',
  {
    id: uuid('id').defaultRandom().primaryKey(),
    actor_id: uuid('actor_id').references(() => users.id, {
      onDelete: 'set null',
    }),
    entity_type: text('entity_type').notNull(),
    entity_id: text('entity_id').notNull(),
    action: text('action').notNull(),
    payload_json: jsonb('payload_json'),
    ip_address: text('ip_address'),
    created_at: timestamp('created_at', { withTimezone: true })
      .notNull()
      .defaultNow(),
  },
  (t) => [
    index('idx_audit_logs_entity').on(t.entity_type, t.entity_id, t.created_at),
    // DDL: CREATE INDEX idx_audit_logs_created_at_brin ON audit_logs USING BRIN (created_at);
    index('idx_audit_logs_created_at_brin').using('brin', t.created_at),
  ],
);

// ============================================================================
// SETTINGS
// ============================================================================

export const settings = pgTable(
  'settings',
  {
    id: uuid('id').defaultRandom().primaryKey(),
    key: text('key').notNull(),
    value_json: jsonb('value_json').notNull(),
    updated_by: uuid('updated_by').references(() => users.id, {
      onDelete: 'set null',
    }),
    created_at: timestamp('created_at', { withTimezone: true })
      .notNull()
      .defaultNow(),
    updated_at: timestamp('updated_at', { withTimezone: true })
      .notNull()
      .defaultNow(),
  },
  (t) => [uniqueIndex('UQ_settings_key').on(t.key)],
);
