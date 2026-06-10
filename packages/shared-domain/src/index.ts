// Barrel for shared-domain. Database-specific tables are NOT re-exported at
// the package root to avoid naming collisions (e.g. `syncAttempts` exists in
// both central and local schemas). Import from sub-paths instead:
//
//   import { users, examVersions } from '@plan-cope/shared-domain/db/central';
//   import { localUsers, studentAttempts } from '@plan-cope/shared-domain/db/local';
//
export { runMigrations } from './db/migrator.js';
export type { MigrationResult, AnyDb } from './db/migrator.js';
export { computeChecksum } from './services/exam.service.js';
export type {
  ExamBlockInput,
  ExamBlockType,
  ExamVersionInput,
} from './services/exam.service.js';
export { seedGeography } from './services/seeders/geography.seeder.js';
export { seedRolesAndUsers } from './services/seeders/users.seeder.js';
export { seedExams } from './services/seeders/exams.seeder.js';
export { seedLocal } from './services/seeders/local.seeder.js';
