import { z } from 'zod';

// ── Users ──

export const userStatusEnum = z.enum(['active', 'inactive', 'suspended']);

export const userInsert = z.object({
  email: z.string().email(),
  password_hash: z.string().min(1),
  full_name: z.string().min(1),
  status: userStatusEnum.optional(),
  last_login_at: z.string().datetime().nullable().optional(),
  deleted_at: z.string().datetime().nullable().optional(),
});

export const userSelect = z.object({
  id: z.string().uuid(),
  email: z.string().email(),
  password_hash: z.string(),
  full_name: z.string(),
  status: userStatusEnum,
  last_login_at: z.string().datetime().nullable(),
  deleted_at: z.string().datetime().nullable(),
  created_at: z.string().datetime(),
  updated_at: z.string().datetime(),
});

export type UserInsert = z.infer<typeof userInsert>;
export type UserSelect = z.infer<typeof userSelect>;

// ── Roles ──

export const roleInsert = z.object({
  code: z.string().min(1),
  name: z.string().min(1),
  description: z.string().nullable().optional(),
});

export const roleSelect = z.object({
  id: z.string().uuid(),
  code: z.string(),
  name: z.string(),
  description: z.string().nullable(),
  created_at: z.string().datetime(),
});

export type RoleInsert = z.infer<typeof roleInsert>;
export type RoleSelect = z.infer<typeof roleSelect>;

// ── User Roles ──

export const userRoleInsert = z.object({
  user_id: z.string().uuid(),
  role_id: z.string().uuid(),
});

export const userRoleSelect = z.object({
  user_id: z.string().uuid(),
  role_id: z.string().uuid(),
  created_at: z.string().datetime(),
});

export type UserRoleInsert = z.infer<typeof userRoleInsert>;
export type UserRoleSelect = z.infer<typeof userRoleSelect>;

// ── Provinces ──

export const provinceInsert = z.object({
  code: z.string().min(1),
  name: z.string().min(1),
});

export const provinceSelect = z.object({
  id: z.string().uuid(),
  code: z.string(),
  name: z.string(),
  created_at: z.string().datetime(),
});

export type ProvinceInsert = z.infer<typeof provinceInsert>;
export type ProvinceSelect = z.infer<typeof provinceSelect>;

// ── Departments ──

export const departmentInsert = z.object({
  province_id: z.string().uuid(),
  code: z.string().min(1),
  name: z.string().min(1),
});

export const departmentSelect = z.object({
  id: z.string().uuid(),
  province_id: z.string().uuid(),
  code: z.string(),
  name: z.string(),
  created_at: z.string().datetime(),
});

export type DepartmentInsert = z.infer<typeof departmentInsert>;
export type DepartmentSelect = z.infer<typeof departmentSelect>;

// ── Localities ──

export const localityInsert = z.object({
  department_id: z.string().uuid(),
  code: z.string().min(1),
  postal_code: z.string().nullable().optional(),
  name: z.string().min(1),
});

export const localitySelect = z.object({
  id: z.string().uuid(),
  department_id: z.string().uuid(),
  code: z.string(),
  postal_code: z.string().nullable(),
  name: z.string(),
  created_at: z.string().datetime(),
});

export type LocalityInsert = z.infer<typeof localityInsert>;
export type LocalitySelect = z.infer<typeof localitySelect>;

// ── Schools ──

export const schoolStatusEnum = z.enum(['active', 'inactive', 'closed']);

export const schoolInsert = z.object({
  code: z.string().min(1),
  cue: z.number().int().positive(),
  annex: z.number().int().nullable().optional(),
  name: z.string().min(1),
  locality_id: z.string().uuid(),
  status: schoolStatusEnum.optional(),
  deleted_at: z.string().datetime().nullable().optional(),
});

export const schoolSelect = z.object({
  id: z.string().uuid(),
  code: z.string(),
  cue: z.number().int().positive(),
  annex: z.number().int().nullable(),
  name: z.string(),
  locality_id: z.string().uuid(),
  status: schoolStatusEnum,
  deleted_at: z.string().datetime().nullable(),
  created_at: z.string().datetime(),
  updated_at: z.string().datetime(),
});

export type SchoolInsert = z.infer<typeof schoolInsert>;
export type SchoolSelect = z.infer<typeof schoolSelect>;

// ── Classrooms ──

export const classroomShiftEnum = z.enum(['morning', 'afternoon', 'night', 'full_day']);

export const classroomInsert = z.object({
  school_id: z.string().uuid(),
  code: z.string().min(1),
  name: z.string().min(1),
  shift: classroomShiftEnum.nullable().optional(),
  metadata_json: z.any().nullable().optional(),
  deleted_at: z.string().datetime().nullable().optional(),
});

export const classroomSelect = z.object({
  id: z.string().uuid(),
  school_id: z.string().uuid(),
  code: z.string(),
  name: z.string(),
  shift: classroomShiftEnum.nullable(),
  metadata_json: z.any().nullable(),
  deleted_at: z.string().datetime().nullable(),
  created_at: z.string().datetime(),
  updated_at: z.string().datetime(),
});

export type ClassroomInsert = z.infer<typeof classroomInsert>;
export type ClassroomSelect = z.infer<typeof classroomSelect>;
