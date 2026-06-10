import { z } from 'zod';

// ── Audit Logs ──

export const auditLogInsert = z.object({
  actor_id: z.string().uuid().nullable().optional(),
  entity_type: z.string().min(1),
  entity_id: z.string().min(1),
  action: z.string().min(1),
  payload_json: z.any().nullable().optional(),
  ip_address: z.string().nullable().optional(),
});

export const auditLogSelect = z.object({
  id: z.string().uuid(),
  actor_id: z.string().uuid().nullable(),
  entity_type: z.string(),
  entity_id: z.string(),
  action: z.string(),
  payload_json: z.any().nullable(),
  ip_address: z.string().nullable(),
  created_at: z.string().datetime(),
});

export type AuditLogInsert = z.infer<typeof auditLogInsert>;
export type AuditLogSelect = z.infer<typeof auditLogSelect>;
