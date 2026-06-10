import { z } from 'zod';
import {
  examVersionStatusEnum,
  blockTypeEnum,
  examBlockOptionSelect,
} from './exam.js';

// ── Sync protocol payloads (camelCase, network-facing) ──

// ── Registered Nodes ──

export const nodeStatusEnum = z.enum(['active', 'inactive', 'blocked']);

export const registeredNodeInsert = z.object({
  school_id: z.string().uuid().nullable().optional(),
  node_code: z.string().min(1),
  device_name: z.string().nullable().optional(),
  status: nodeStatusEnum.optional(),
  last_seen_at: z.string().datetime().nullable().optional(),
});

export const registeredNodeSelect = z.object({
  id: z.string().uuid(),
  school_id: z.string().uuid().nullable(),
  node_code: z.string(),
  device_name: z.string().nullable(),
  status: nodeStatusEnum,
  last_seen_at: z.string().datetime().nullable(),
  created_at: z.string().datetime(),
  updated_at: z.string().datetime(),
});

export type RegisteredNodeInsert = z.infer<typeof registeredNodeInsert>;
export type RegisteredNodeSelect = z.infer<typeof registeredNodeSelect>;

// ── Central Delivery Sessions ──

export const deliverySessionStatusEnum = z.enum(['pending', 'active', 'completed', 'cancelled']);

export const centralDeliverySessionInsert = z.object({
  remote_local_id: z.string().min(1),
  school_id: z.string().uuid().nullable().optional(),
  exam_version_id: z.string().uuid().nullable().optional(),
  classroom_code: z.string().nullable().optional(),
  commission_code: z.string().nullable().optional(),
  status: deliverySessionStatusEnum,
  started_at: z.string().datetime().nullable().optional(),
  ended_at: z.string().datetime().nullable().optional(),
  synced_at: z.string().datetime().nullable().optional(),
});

export const centralDeliverySessionSelect = z.object({
  id: z.string().uuid(),
  remote_local_id: z.string(),
  school_id: z.string().uuid().nullable(),
  exam_version_id: z.string().uuid().nullable(),
  classroom_code: z.string().nullable(),
  commission_code: z.string().nullable(),
  status: deliverySessionStatusEnum,
  started_at: z.string().datetime().nullable(),
  ended_at: z.string().datetime().nullable(),
  synced_at: z.string().datetime().nullable(),
  created_at: z.string().datetime(),
});

export type CentralDeliverySessionInsert = z.infer<typeof centralDeliverySessionInsert>;
export type CentralDeliverySessionSelect = z.infer<typeof centralDeliverySessionSelect>;

// ── Received Student Attempts ──

export const attemptStatusEnum = z.enum(['pending', 'in_progress', 'submitted', 'graded']);

export const receivedStudentAttemptInsert = z.object({
  remote_local_id: z.string().min(1),
  delivery_session_id: z.string().uuid().nullable().optional(),
  student_code: z.string().min(1),
  status: attemptStatusEnum,
  started_at: z.string().datetime().nullable().optional(),
  submitted_at: z.string().datetime().nullable().optional(),
  idempotency_key: z.string().min(1),
});

export const receivedStudentAttemptSelect = z.object({
  id: z.string().uuid(),
  remote_local_id: z.string(),
  delivery_session_id: z.string().uuid().nullable(),
  student_code: z.string(),
  status: attemptStatusEnum,
  started_at: z.string().datetime().nullable(),
  submitted_at: z.string().datetime().nullable(),
  received_at: z.string().datetime(),
  idempotency_key: z.string(),
  created_at: z.string().datetime(),
});

export type ReceivedStudentAttemptInsert = z.infer<typeof receivedStudentAttemptInsert>;
export type ReceivedStudentAttemptSelect = z.infer<typeof receivedStudentAttemptSelect>;

// ── Received Submission Answers ──

export const receivedSubmissionAnswerInsert = z.object({
  student_attempt_id: z.string().uuid(),
  block_id: z.string().uuid(),
  answer_json: z.any(),
});

export const receivedSubmissionAnswerSelect = z.object({
  id: z.string().uuid(),
  student_attempt_id: z.string().uuid(),
  block_id: z.string().uuid(),
  answer_json: z.any(),
  created_at: z.string().datetime(),
});

export type ReceivedSubmissionAnswerInsert = z.infer<typeof receivedSubmissionAnswerInsert>;
export type ReceivedSubmissionAnswerSelect = z.infer<typeof receivedSubmissionAnswerSelect>;

// ── Sync Inbox ──

export const inboxStatusEnum = z.enum(['pending', 'processing', 'completed', 'failed', 'discarded']);

export const syncInboxInsert = z.object({
  source_node_id: z.string().uuid().nullable().optional(),
  event_type: z.string().min(1),
  aggregate_type: z.string().min(1),
  aggregate_id: z.string().min(1),
  idempotency_key: z.string().min(1),
  payload_json: z.any(),
  status: inboxStatusEnum.optional(),
  processed_at: z.string().datetime().nullable().optional(),
});

export const syncInboxSelect = z.object({
  id: z.string().uuid(),
  source_node_id: z.string().uuid().nullable(),
  event_type: z.string(),
  aggregate_type: z.string(),
  aggregate_id: z.string(),
  idempotency_key: z.string(),
  payload_json: z.any(),
  status: inboxStatusEnum,
  created_at: z.string().datetime(),
  processed_at: z.string().datetime().nullable(),
});

export type SyncInboxInsert = z.infer<typeof syncInboxInsert>;
export type SyncInboxSelect = z.infer<typeof syncInboxSelect>;

// ── Sync Cursors ──

export const syncCursorInsert = z.object({
  node_id: z.string().uuid(),
  cursor_key: z.string().min(1),
  cursor_value: z.string().min(1),
});

export const syncCursorSelect = z.object({
  id: z.string().uuid(),
  node_id: z.string().uuid(),
  cursor_key: z.string(),
  cursor_value: z.string(),
  updated_at: z.string().datetime(),
});

export type SyncCursorInsert = z.infer<typeof syncCursorInsert>;
export type SyncCursorSelect = z.infer<typeof syncCursorSelect>;

// ── Sync Attempts ──

export const syncDirectionEnum = z.enum(['inbound', 'outbound']);
export const syncAttemptStatusEnum = z.enum(['running', 'completed', 'failed']);

export const syncAttemptInsert = z.object({
  node_id: z.string().uuid().nullable().optional(),
  direction: syncDirectionEnum,
  status: syncAttemptStatusEnum,
  started_at: z.string().datetime().optional(),
  finished_at: z.string().datetime().nullable().optional(),
  summary_json: z.any().nullable().optional(),
  error_json: z.any().nullable().optional(),
});

export const syncAttemptSelect = z.object({
  id: z.string().uuid(),
  node_id: z.string().uuid().nullable(),
  direction: syncDirectionEnum,
  status: syncAttemptStatusEnum,
  started_at: z.string().datetime(),
  finished_at: z.string().datetime().nullable(),
  summary_json: z.any().nullable(),
  error_json: z.any().nullable(),
});

export type SyncAttemptInsert = z.infer<typeof syncAttemptInsert>;
export type SyncAttemptSelect = z.infer<typeof syncAttemptSelect>;

// ── Sync Protocol (Pull / Push) ──
//
// The Pull / Push payloads are network-facing objects. Field names use
// camelCase (per spec), not the snake_case used by DB columns. They reference
// the existing select schemas for individual aggregates so that the protocol
// contract stays aligned with the DDL_V3 row shape.

const examVersionChange = z.object({
  id: z.string().uuid(),
  exam_id: z.string().uuid(),
  version_number: z.number().int().positive(),
  schema_version: z.number().int().positive(),
  status: examVersionStatusEnum,
  metadata_json: z.unknown().nullable().optional(),
  created_by: z.string().uuid().nullable().optional(),
  reviewed_by: z.string().uuid().nullable().optional(),
  approved_by: z.string().uuid().nullable().optional(),
  published_by: z.string().uuid().nullable().optional(),
  published_at: z.string().datetime().nullable().optional(),
  created_at: z.string().datetime(),
  updated_at: z.string().datetime(),
});
export type ExamVersionChange = z.infer<typeof examVersionChange>;

const examBlockChange = z.object({
  id: z.string().uuid(),
  exam_version_id: z.string().uuid(),
  order_index: z.number().int().nonnegative(),
  block_type: blockTypeEnum,
  title: z.string().nullable().optional(),
  description: z.string().nullable().optional(),
  config_json: z.unknown(),
  validation_json: z.unknown().nullable().optional(),
  options: z.array(examBlockOptionSelect.omit({ exam_block_id: true, created_at: true, updated_at: true })).optional(),
});
export type ExamBlockChange = z.infer<typeof examBlockChange>;

const answerKeyChange = z.object({
  id: z.string().uuid(),
  exam_block_id: z.string().uuid(),
  correct_answer_json: z.unknown(),
  score_value: z.number().nullable().optional(),
  metadata_json: z.unknown().nullable().optional(),
});
export type AnswerKeyChange = z.infer<typeof answerKeyChange>;

const examAssetChange = z.object({
  id: z.string().uuid(),
  exam_version_id: z.string().uuid(),
  file_name: z.string(),
  mime_type: z.string(),
  size_bytes: z.number().int().positive(),
  checksum: z.string(),
  storage_path: z.string(),
  created_at: z.string().datetime(),
});
export type ExamAssetChange = z.infer<typeof examAssetChange>;

const syncChanges = z.object({
  examVersions: z.array(examVersionChange),
  examBlocks: z.array(examBlockChange),
  answerKeys: z.array(answerKeyChange),
  assets: z.array(examAssetChange),
});
export type SyncChanges = z.infer<typeof syncChanges>;

export const SyncPullResponse = z.object({
  cursor: z.number().int().nonnegative(),
  changes: syncChanges,
  checksums: z.record(z.string(), z.string()),
});
export type SyncPullResponse = z.infer<typeof SyncPullResponse>;

const syncAnswer = z.object({
  block_id: z.string().uuid(),
  answer_json: z.unknown(),
});
export type SyncAnswer = z.infer<typeof syncAnswer>;

export const AttemptPayload = z.object({
  remote_local_id: z.string().min(1),
  student_code: z.string().min(1),
  status: attemptStatusEnum,
  started_at: z.string().datetime().nullable().optional(),
  submitted_at: z.string().datetime().nullable().optional(),
  answers: z.array(syncAnswer),
});
export type AttemptPayload = z.infer<typeof AttemptPayload>;

export const SyncPushRequest = z.object({
  idempotencyKey: z.string().uuid(),
  attempts: z.array(AttemptPayload).min(1),
});
export type SyncPushRequest = z.infer<typeof SyncPushRequest>;

const pushRejection = z.object({
  remote_local_id: z.string().min(1),
  reason: z.string().min(1),
});
export type PushRejection = z.infer<typeof pushRejection>;

export const SyncPushResponse = z.object({
  accepted: z.number().int().nonnegative(),
  rejected: z.array(pushRejection),
  cursor: z.number().int().nonnegative(),
});
export type SyncPushResponse = z.infer<typeof SyncPushResponse>;
