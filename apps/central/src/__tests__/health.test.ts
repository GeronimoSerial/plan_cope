import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { existsSync } from 'node:fs';
import { resolve } from 'node:path';
import postgres from 'postgres';
import { drizzle as drizzlePg } from 'drizzle-orm/postgres-js';
import { runMigrations } from '@plan-cope/shared-domain/db/migrator';

const CENTRAL_MIGRATIONS = resolve(
  '/home/gero/Documentos/GitHub/Plan Cope/packages/shared-domain/src/db/migrations/central',
);
const CENTRAL_URL = 'postgres://plan_cope:test@localhost:5432/plan_cope_test';

const isPgUp = async (): Promise<boolean> => {
  try {
    const sql = postgres(CENTRAL_URL, { max: 1, idle_timeout: 1 });
    await sql`select 1`;
    await sql.end();
    return true;
  } catch {
    return false;
  }
};

describe('central app: GET /api/health', () => {
  let pgUp = false;
  let sql: ReturnType<typeof postgres> | null = null;

  beforeAll(async () => {
    pgUp = await isPgUp();
    if (!pgUp) return;
    sql = postgres(CENTRAL_URL, { max: 5 });
    await sql.unsafe(`
      DROP SCHEMA IF EXISTS public CASCADE;
      CREATE SCHEMA public;
      DROP SCHEMA IF EXISTS drizzle CASCADE;
    `);
  });

  afterAll(async () => {
    if (sql) await sql.end();
  });

  it('returns 200 with status:ok, db:connected, uptime, version', async () => {
    if (!pgUp) {
      console.warn('skipping: Postgres not reachable');
      return;
    }
    if (!existsSync(CENTRAL_MIGRATIONS)) {
      throw new Error(`central migrations missing at ${CENTRAL_MIGRATIONS}`);
    }
    const db = drizzlePg(sql!);
    await runMigrations(db, CENTRAL_MIGRATIONS);

    const { buildCentralApp } = await import('../server/app.js');
    const app = await buildCentralApp({ db, skipMigrations: true });
    try {
      const res = await app.inject({ method: 'GET', url: '/api/health' });
      expect(res.statusCode).toBe(200);
      const body = res.json();
      expect(body).toMatchObject({ status: 'ok', db: 'connected' });
      expect(typeof body.uptime).toBe('number');
      expect(body.uptime).toBeGreaterThanOrEqual(0);
      expect(typeof body.version).toBe('string');
      expect(body.version).toMatch(/0\.1\./);
    } finally {
      await app.close();
    }
  });

  it('serves health endpoint with skipMigrations:true (test mode)', async () => {
    if (!pgUp) {
      console.warn('skipping: Postgres not reachable');
      return;
    }
    const db = drizzlePg(sql!);
    const { buildCentralApp } = await import('../server/app.js');
    const app = await buildCentralApp({ db, skipMigrations: true });
    try {
      const res = await app.inject({ method: 'GET', url: '/api/health' });
      expect(res.statusCode).toBe(200);
      expect(res.json().db).toBe('connected');
    } finally {
      await app.close();
    }
  });
});
