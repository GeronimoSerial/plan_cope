/**
 * Central PostgreSQL Drizzle relations.
 *
 * Enables `db.query.users.findMany({ with: { roles: true } })` API.
 * Mirrors the FK graph from DDL_V3.sql.
 */
import { relations } from 'drizzle-orm';
import {
  assetUsages,
  answerKeys,
  auditLogs,
  centralDeliverySessions,
  classrooms,
  departments,
  examAssets,
  examBlockOptions,
  examBlocks,
  examVersions,
  exams,
  localities,
  provinces,
  publicationPackages,
  publicationTargets,
  receivedStudentAttempts,
  receivedSubmissionAnswers,
  registeredNodes,
  roles,
  schools,
  settings,
  syncAttempts,
  syncCursors,
  syncInbox,
  userRoles,
  users,
} from './schema.js';

// ── users / roles ──
export const usersRelations = relations(users, ({ many }) => ({
  userRoles: many(userRoles),
  createdVersions: many(examVersions, { relationName: 'examVersionsCreatedBy' }),
  reviewedVersions: many(examVersions, { relationName: 'examVersionsReviewedBy' }),
  approvedVersions: many(examVersions, { relationName: 'examVersionsApprovedBy' }),
  publishedVersions: many(examVersions, { relationName: 'examVersionsPublishedBy' }),
  auditLogs: many(auditLogs),
  settingsUpdates: many(settings),
}));

export const rolesRelations = relations(roles, ({ many }) => ({
  userRoles: many(userRoles),
}));

export const userRolesRelations = relations(userRoles, ({ one }) => ({
  user: one(users, { fields: [userRoles.user_id], references: [users.id] }),
  role: one(roles, { fields: [userRoles.role_id], references: [roles.id] }),
}));

// ── geography ──
export const provincesRelations = relations(provinces, ({ many }) => ({
  departments: many(departments),
}));

export const departmentsRelations = relations(departments, ({ one, many }) => ({
  province: one(provinces, {
    fields: [departments.province_id],
    references: [provinces.id],
  }),
  localities: many(localities),
}));

export const localitiesRelations = relations(localities, ({ one, many }) => ({
  department: one(departments, {
    fields: [localities.department_id],
    references: [departments.id],
  }),
  schools: many(schools),
}));

export const schoolsRelations = relations(schools, ({ one, many }) => ({
  locality: one(localities, {
    fields: [schools.locality_id],
    references: [localities.id],
  }),
  classrooms: many(classrooms),
  registeredNodes: many(registeredNodes),
  deliverySessions: many(centralDeliverySessions),
}));

export const classroomsRelations = relations(classrooms, ({ one }) => ({
  school: one(schools, { fields: [classrooms.school_id], references: [schools.id] }),
}));

// ── exams ──
export const examsRelations = relations(exams, ({ many }) => ({
  versions: many(examVersions),
}));

export const examVersionsRelations = relations(examVersions, ({ one, many }) => ({
  exam: one(exams, { fields: [examVersions.exam_id], references: [exams.id] }),
  blocks: many(examBlocks),
  assets: many(examAssets),
  publicationPackages: many(publicationPackages),
  deliverySessions: many(centralDeliverySessions),
  createdByUser: one(users, {
    fields: [examVersions.created_by],
    references: [users.id],
    relationName: 'examVersionsCreatedBy',
  }),
  reviewedByUser: one(users, {
    fields: [examVersions.reviewed_by],
    references: [users.id],
    relationName: 'examVersionsReviewedBy',
  }),
  approvedByUser: one(users, {
    fields: [examVersions.approved_by],
    references: [users.id],
    relationName: 'examVersionsApprovedBy',
  }),
  publishedByUser: one(users, {
    fields: [examVersions.published_by],
    references: [users.id],
    relationName: 'examVersionsPublishedBy',
  }),
}));

export const examBlocksRelations = relations(examBlocks, ({ one, many }) => ({
  examVersion: one(examVersions, {
    fields: [examBlocks.exam_version_id],
    references: [examVersions.id],
  }),
  options: many(examBlockOptions),
  answerKeys: many(answerKeys),
  assetUsages: many(assetUsages),
  submissionAnswers: many(receivedSubmissionAnswers),
}));

export const examBlockOptionsRelations = relations(examBlockOptions, ({ one }) => ({
  block: one(examBlocks, {
    fields: [examBlockOptions.exam_block_id],
    references: [examBlocks.id],
  }),
}));

export const answerKeysRelations = relations(answerKeys, ({ one }) => ({
  block: one(examBlocks, {
    fields: [answerKeys.exam_block_id],
    references: [examBlocks.id],
  }),
}));

// ── assets ──
export const examAssetsRelations = relations(examAssets, ({ one, many }) => ({
  examVersion: one(examVersions, {
    fields: [examAssets.exam_version_id],
    references: [examVersions.id],
  }),
  assetUsages: many(assetUsages),
}));

export const assetUsagesRelations = relations(assetUsages, ({ one }) => ({
  block: one(examBlocks, {
    fields: [assetUsages.exam_block_id],
    references: [examBlocks.id],
  }),
  asset: one(examAssets, {
    fields: [assetUsages.exam_asset_id],
    references: [examAssets.id],
  }),
}));

// ── publication ──
export const publicationPackagesRelations = relations(
  publicationPackages,
  ({ one, many }) => ({
    examVersion: one(examVersions, {
      fields: [publicationPackages.exam_version_id],
      references: [examVersions.id],
    }),
    targets: many(publicationTargets),
  }),
);

export const publicationTargetsRelations = relations(publicationTargets, ({ one }) => ({
  package: one(publicationPackages, {
    fields: [publicationTargets.publication_package_id],
    references: [publicationPackages.id],
  }),
}));

// ── sync / nodes ──
export const registeredNodesRelations = relations(registeredNodes, ({ one, many }) => ({
  school: one(schools, {
    fields: [registeredNodes.school_id],
    references: [schools.id],
  }),
  syncInboxEntries: many(syncInbox),
  cursors: many(syncCursors),
  syncAttempts: many(syncAttempts),
  deliverySessions: many(centralDeliverySessions),
}));

export const centralDeliverySessionsRelations = relations(
  centralDeliverySessions,
  ({ one, many }) => ({
    school: one(schools, {
      fields: [centralDeliverySessions.school_id],
      references: [schools.id],
    }),
    examVersion: one(examVersions, {
      fields: [centralDeliverySessions.exam_version_id],
      references: [examVersions.id],
    }),
    receivedAttempts: many(receivedStudentAttempts),
  }),
);

export const receivedStudentAttemptsRelations = relations(
  receivedStudentAttempts,
  ({ one, many }) => ({
    deliverySession: one(centralDeliverySessions, {
      fields: [receivedStudentAttempts.delivery_session_id],
      references: [centralDeliverySessions.id],
    }),
    answers: many(receivedSubmissionAnswers),
  }),
);

export const receivedSubmissionAnswersRelations = relations(
  receivedSubmissionAnswers,
  ({ one }) => ({
    attempt: one(receivedStudentAttempts, {
      fields: [receivedSubmissionAnswers.student_attempt_id],
      references: [receivedStudentAttempts.id],
    }),
    block: one(examBlocks, {
      fields: [receivedSubmissionAnswers.block_id],
      references: [examBlocks.id],
    }),
  }),
);

export const syncInboxRelations = relations(syncInbox, ({ one }) => ({
  sourceNode: one(registeredNodes, {
    fields: [syncInbox.source_node_id],
    references: [registeredNodes.id],
  }),
}));

export const syncCursorsRelations = relations(syncCursors, ({ one }) => ({
  node: one(registeredNodes, {
    fields: [syncCursors.node_id],
    references: [registeredNodes.id],
  }),
}));

export const syncAttemptsRelations = relations(syncAttempts, ({ one }) => ({
  node: one(registeredNodes, {
    fields: [syncAttempts.node_id],
    references: [registeredNodes.id],
  }),
}));

// ── audit / settings ──
export const auditLogsRelations = relations(auditLogs, ({ one }) => ({
  actor: one(users, { fields: [auditLogs.actor_id], references: [users.id] }),
}));

export const settingsRelations = relations(settings, ({ one }) => ({
  updatedByUser: one(users, {
    fields: [settings.updated_by],
    references: [users.id],
  }),
}));
