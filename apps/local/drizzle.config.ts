import { defineConfig } from 'drizzle-kit';

export default defineConfig({
  schema: '../../packages/shared-domain/src/db/local/schema.ts',
  out: '../../packages/shared-domain/src/db/migrations/local',
  dialect: 'sqlite',
  dbCredentials: {
    url: process.env['LOCAL_DB_PATH'] ?? './data/local.db',
  },
  verbose: true,
  strict: true,
});
