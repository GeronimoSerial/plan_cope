/**
 * Local node seeder — SQLite (nodo local).
 *
 * Inserts:
 *   - 2 local_users (admin / operator) with bcrypt-hashed passwords
 *   - 4 app_settings (central_url, jwt_secret, sync_interval, node_label)
 *
 * Idempotent: re-running is a no-op via `INSERT OR IGNORE`.
 */
import { randomBytes } from 'node:crypto';
import bcrypt from 'bcrypt';
import { sql } from 'drizzle-orm';
import type { BetterSQLite3Database } from 'drizzle-orm/better-sqlite3';
import { appSettings, localUsers } from '../../db/local/schema.js';

const BCRYPT_COST = 10;

const generateJwtSecret = (): string =>
  randomBytes(32).toString('hex');

const nowIso = (): string => new Date().toISOString();

const uuid = (): string => crypto.randomUUID();

interface LocalUserDef {
  id: string;
  username: string;
  fullName: string;
  role: 'admin' | 'operator';
  password: string;
}

const USER_DEFS: ReadonlyArray<LocalUserDef> = [
  {
    id: uuid(),
    username: 'admin',
    fullName: 'Administrador Local',
    role: 'admin',
    password: 'admin123',
  },
  {
    id: uuid(),
    username: 'operator',
    fullName: 'Operador de Sede',
    role: 'operator',
    password: 'operator123',
  },
];

const buildSettings = (): Array<{ key: string; value_json: string }> => {
  const settings: Array<{ key: string; value_json: string }> = [
    { key: 'central_url', value_json: JSON.stringify('http://localhost:3000') },
    { key: 'jwt_secret', value_json: JSON.stringify(generateJwtSecret()) },
    { key: 'sync_interval', value_json: JSON.stringify(60) },
    { key: 'node_label', value_json: JSON.stringify('Nodo Local (placeholder)') },
  ];
  return settings;
};

export const seedLocal = async (db: BetterSQLite3Database): Promise<void> => {
  // 1. Local users — idempotent on (username) which is UNIQUE.
  for (const u of USER_DEFS) {
    const passwordHash = await bcrypt.hash(u.password, BCRYPT_COST);
    await db
      .insert(localUsers)
      .values({
        id: u.id,
        username: u.username,
        password_hash: passwordHash,
        role: u.role,
        active: 1,
        created_at: nowIso(),
      })
      .onConflictDoNothing({ target: localUsers.username });
  }

  // 2. App settings — idempotent on (key) which is UNIQUE.
  for (const s of buildSettings()) {
    await db
      .insert(appSettings)
      .values({
        id: uuid(),
        key: s.key,
        value_json: s.value_json,
        updated_at: nowIso(),
      })
      .onConflictDoNothing({ target: appSettings.key });
  }
};

// Re-export to silence unused-import warnings under strict mode.
void sql;
