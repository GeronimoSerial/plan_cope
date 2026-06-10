import { z } from 'zod';

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
