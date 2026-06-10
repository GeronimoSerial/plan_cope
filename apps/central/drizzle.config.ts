import { defineConfig } from 'drizzle-kit';

export default defineConfig({
  schema: '../../packages/shared-domain/src/db/central/schema.ts',
  out: '../../packages/shared-domain/src/db/migrations/central',
  dialect: 'postgresql',
  dbCredentials: {
    url: process.env['DATABASE_URL'] ?? 'postgres://plan_cope:test@localhost:5432/plan_cope',
  },
  verbose: true,
  strict: true,
});
