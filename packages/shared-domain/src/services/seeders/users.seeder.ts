/**
 * Roles + users seeder — central PostgreSQL.
 *
 * Inserts 3 roles (admin / operator / viewer) and 3 users (one per
 * role) with bcrypt-hashed passwords. Idempotent: re-running the
 * seeder is a no-op via `ON CONFLICT DO NOTHING` on the unique
 * `users.email` and `roles.code` columns.
 *
 * Password policy (Fase 1 placeholders — Fase 2 wires real auth):
 *   admin    → "admin123"    (rotate before any real environment)
 *   operator → "operator123"
 *   viewer   → "viewer123"
 */
import { sql } from 'drizzle-orm';
import bcrypt from 'bcrypt';
import type { PostgresJsDatabase } from 'drizzle-orm/postgres-js';
import { roles, userRoles, users } from '../../db/central/schema.js';

const BCRYPT_COST = 10; // Cheap enough for seeders, strong enough for dev.

interface RoleDef {
  code: 'admin' | 'operator' | 'viewer';
  name: string;
  description: string;
  email: string;
  fullName: string;
  password: string;
}

const ROLE_DEFS: ReadonlyArray<RoleDef> = [
  {
    code: 'admin',
    name: 'Administrator',
    description: 'Acceso total a la plataforma',
    email: 'admin@plancope.local',
    fullName: 'Administrador',
    password: 'admin123',
  },
  {
    code: 'operator',
    name: 'Operator',
    description: 'Gestión de contenido y visualización de resultados',
    email: 'operator@plancope.local',
    fullName: 'Operador',
    password: 'operator123',
  },
  {
    code: 'viewer',
    name: 'Viewer',
    description: 'Solo lectura de resultados y reportes',
    email: 'viewer@plancope.local',
    fullName: 'Visualizador',
    password: 'viewer123',
  },
];

const findRoleId = async (
  db: PostgresJsDatabase,
  code: RoleDef['code'],
): Promise<string> => {
  const rows = await db.execute<{ id: string }>(
    sql`SELECT id FROM roles WHERE code = ${code} LIMIT 1`,
  );
  const id = rows.at(0)?.id;
  if (!id) throw new Error(`seedRolesAndUsers: role "${code}" not found`);
  return id;
};

const findUserId = async (
  db: PostgresJsDatabase,
  email: string,
): Promise<string> => {
  const rows = await db.execute<{ id: string }>(
    sql`SELECT id FROM users WHERE email = ${email} LIMIT 1`,
  );
  const id = rows.at(0)?.id;
  if (!id) throw new Error(`seedRolesAndUsers: user "${email}" not found`);
  return id;
};

export const seedRolesAndUsers = async (
  db: PostgresJsDatabase,
): Promise<void> => {
  // 1. Roles — upsert by unique code.
  for (const r of ROLE_DEFS) {
    await db
      .insert(roles)
      .values({ code: r.code, name: r.name, description: r.description })
      .onConflictDoNothing({ target: roles.code });
  }

  // 2. Users — one per role, with a bcrypt-hashed password.
  for (const r of ROLE_DEFS) {
    const passwordHash = await bcrypt.hash(r.password, BCRYPT_COST);
    await db
      .insert(users)
      .values({
        email: r.email,
        password_hash: passwordHash,
        full_name: r.fullName,
      })
      .onConflictDoNothing({ target: users.email });
  }

  // 3. user_roles — link each user to their role.
  for (const r of ROLE_DEFS) {
    const userId = await findUserId(db, r.email);
    const roleId = await findRoleId(db, r.code);
    await db
      .insert(userRoles)
      .values({ user_id: userId, role_id: roleId })
      .onConflictDoNothing();
  }
};
