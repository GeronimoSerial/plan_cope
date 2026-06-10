import { z } from 'zod';

// ── Settings ──

// `value_json` is a JSON column (NOT NULL in DDL). Accept any JSON value but
// reject `undefined` (missing) and `undefined` values explicitly.
const jsonValue = z.union([
  z.string(),
  z.number(),
  z.boolean(),
  z.null(),
  z.array(z.unknown()),
  z.record(z.string(), z.unknown()),
]);

export const settingInsert = z.object({
  key: z.string().min(1),
  value_json: jsonValue,
  updated_by: z.string().uuid().nullable().optional(),
});

export const settingSelect = z.object({
  id: z.string().uuid(),
  key: z.string(),
  value_json: jsonValue,
  updated_by: z.string().uuid().nullable(),
  created_at: z.string().datetime(),
  updated_at: z.string().datetime(),
});

export type SettingInsert = z.infer<typeof settingInsert>;
export type SettingSelect = z.infer<typeof settingSelect>;
