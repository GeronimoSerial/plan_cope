/**
 * Local SQLite Drizzle relations. Mirrors the FK graph in DDL_V3.sql.
 */
import { relations } from 'drizzle-orm';
import {
  appSettings,
  deliverySessions,
  localAnswerKeys,
  localAssets,
  localAuditLogs,
  localExamBlocks,
  localExamVersions,
  studentAttempts,
  submissionAnswers,
  syncAttempts,
  syncOutbox,
  syncState,
} from './schema.js';

export const localExamVersionsRelations = relations(
  localExamVersions,
  ({ many }) => ({
    blocks: many(localExamBlocks),
    answerKeys: many(localAnswerKeys),
    assets: many(localAssets),
  }),
);

export const localExamBlocksRelations = relations(localExamBlocks, ({ one, many }) => ({
  examVersion: one(localExamVersions, {
    fields: [localExamBlocks.local_exam_version_id],
    references: [localExamVersions.id],
  }),
  submissionAnswers: many(submissionAnswers),
}));

export const localAnswerKeysRelations = relations(localAnswerKeys, ({ one }) => ({
  examVersion: one(localExamVersions, {
    fields: [localAnswerKeys.local_exam_version_id],
    references: [localExamVersions.id],
  }),
}));

export const localAssetsRelations = relations(localAssets, ({ one }) => ({
  examVersion: one(localExamVersions, {
    fields: [localAssets.local_exam_version_id],
    references: [localExamVersions.id],
  }),
}));

export const deliverySessionsRelations = relations(deliverySessions, ({ many }) => ({
  attempts: many(studentAttempts),
}));

export const studentAttemptsRelations = relations(studentAttempts, ({ one, many }) => ({
  deliverySession: one(deliverySessions, {
    fields: [studentAttempts.delivery_session_id],
    references: [deliverySessions.id],
  }),
  answers: many(submissionAnswers),
}));

export const submissionAnswersRelations = relations(submissionAnswers, ({ one }) => ({
  attempt: one(studentAttempts, {
    fields: [submissionAnswers.student_attempt_id],
    references: [studentAttempts.id],
  }),
  block: one(localExamBlocks, {
    fields: [submissionAnswers.block_id],
    references: [localExamBlocks.id],
  }),
}));

// Flat tables (queue, state, settings, audit) have no relations to declare.
// They're exported as identity stubs so callers can `drizzle({ ..., relations: [...all] })`.
export const syncOutboxRelations = relations(syncOutbox, () => ({}));
export const syncStateRelations = relations(syncState, () => ({}));
export const syncAttemptsRelations = relations(syncAttempts, () => ({}));
export const localAuditLogsRelations = relations(localAuditLogs, () => ({}));
export const appSettingsRelations = relations(appSettings, () => ({}));
