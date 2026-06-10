import { z } from 'zod';

// ── Settings ──

export const settingInsert = z.object({
  key: z.string().min(1),
  value_json: z.any(),
  updated_by: z.string().uuid().nullable().optional(),
});

export const settingSelect = z.object({
  id: z.string().uuid(),
  key: z.string(),
  value_json: z.any(),
  updated_by: z.string().uuid().nullable(),
  created_at: z.string().datetime(),
  updated_at: z.string().datetime(),
});

export type SettingInsert = z.infer<typeof settingInsert>;
export type SettingSelect = z.infer<typeof settingSelect>;
