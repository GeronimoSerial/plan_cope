import { describe, expect, it } from 'vitest';
import { existsSync } from 'node:fs';
import { resolve } from 'node:path';
import Database from 'better-sqlite3';
import { drizzle as drizzleSqlite } from 'drizzle-orm/better-sqlite3';
import { runMigrations } from '@plan-cope/shared-domain/db/migrator';

const LOCAL_MIGRATIONS = resolve(
  '/home/gero/Documentos/GitHub/Plan Cope/packages/shared-domain/src/db/migrations/local',
);

describe('local app: GET /api/health', () => {
  it('returns 200 with status:ok, db:connected, uptime, version', async () => {
    if (!existsSync(LOCAL_MIGRATIONS)) {
      throw new Error(`local migrations missing at ${LOCAL_MIGRATIONS}`);
    }
    const sqlite = new Database(':memory:');
    const db = drizzleSqlite(sqlite);
    try {
      await runMigrations(db, LOCAL_MIGRATIONS);
      const { buildLocalApp } = await import('../server/app.js');
      const app = await buildLocalApp({ db, skipMigrations: true });
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
    } finally {
      sqlite.close();
    }
  });

  it('serves health endpoint with skipMigrations:true (test mode)', async () => {
    const sqlite = new Database(':memory:');
    const db = drizzleSqlite(sqlite);
    try {
      const { buildLocalApp } = await import('../server/app.js');
      const app = await buildLocalApp({ db, skipMigrations: true });
      try {
        const res = await app.inject({ method: 'GET', url: '/api/health' });
        expect(res.statusCode).toBe(200);
        expect(res.json().db).toBe('connected');
      } finally {
        await app.close();
      }
    } finally {
      sqlite.close();
    }
  });
});
