import "server-only";
import { centralFetch } from "../server/central-server";
import { getAccessToken } from "../server/session";
import { extractApiError } from "../json";
import type { ExamSummary, ExamVersion, InstallerReference } from "../contracts";

class SessionExpiredError extends Error {
  constructor() {
    super("Sesion expirada.");
    this.name = "SessionExpiredError";
  }
}

class NotFoundError extends Error {
  constructor() {
    super("Recurso no encontrado.");
    this.name = "NotFoundError";
  }
}

class ScopeDeniedError extends Error {
  constructor() {
    super("No tenés alcance asignado para esta consulta.");
    this.name = "ScopeDeniedError";
  }
}

class NoInstallerPublishedError extends Error {
  constructor() {
    super("No installer published yet.");
    this.name = "NoInstallerPublishedError";
  }
}

export function isSessionExpired(error: unknown): boolean {
  return error instanceof SessionExpiredError;
}

export function isNotFound(error: unknown): boolean {
  return error instanceof NotFoundError;
}

export function isScopeDenied(error: unknown): boolean {
  return error instanceof ScopeDeniedError;
}

export function isNoInstallerPublishedError(error: unknown): boolean {
  return error instanceof NoInstallerPublishedError;
}

// Lectura de datos desde server components / loaders. Usa el token de la cookie httpOnly.
async function serverGet<T>(path: string): Promise<T> {
  const token = await getAccessToken();
  if (!token) {
    throw new SessionExpiredError();
  }

  const res = await centralFetch(path, { accessToken: token });
  if (res.status === 401) {
    throw new SessionExpiredError();
  }

  const text = await res.text();
  if (res.status === 404) {
    throw new NotFoundError();
  }
  if (res.status === 403) {
    throw new ScopeDeniedError();
  }
  if (!res.ok) {
    throw new Error(extractApiError(text, res.status));
  }

  return (text ? (JSON.parse(text) as T) : (undefined as T));
}

export function listExams(): Promise<ExamSummary[]> {
  return serverGet<ExamSummary[]>("/api/exams");
}

export function listVersions(examId: string): Promise<ExamVersion[]> {
  return serverGet<ExamVersion[]>(`/api/exams/${encodeURIComponent(examId)}/versions`);
}

export function getVersion(versionId: string): Promise<ExamVersion> {
  return serverGet<ExamVersion>(`/api/exams/versions/${encodeURIComponent(versionId)}`);
}

// Ultimo instalador publicado para un canal (B7.T15). El API responde 503 cuando el storage
// no esta configurado o todavia no hay nada publicado: se traduce en NoInstallerPublishedError
// para que la pagina distinga "no publicado" de un error real de conexion.
export async function getLatestInstaller(channel = "stable"): Promise<InstallerReference> {
  const token = await getAccessToken();
  if (!token) {
    throw new SessionExpiredError();
  }

  const res = await centralFetch(`/api/downloads/installer/latest?channel=${encodeURIComponent(channel)}`, {
    accessToken: token
  });
  if (res.status === 401) {
    throw new SessionExpiredError();
  }

  const text = await res.text();
  if (res.status === 503) {
    throw new NoInstallerPublishedError();
  }
  if (!res.ok) {
    throw new Error(extractApiError(text, res.status));
  }

  return text ? (JSON.parse(text) as InstallerReference) : (undefined as unknown as InstallerReference);
}

export interface ActivationKeySummary {
  id: string;
  keyPrefix: string;
  issuedAt: string;
  expiresAt?: string | null;
  maxActivations: number;
  activationCount: number;
  revokedAt?: string | null;
  revokedReason?: string | null;
  note?: string | null;
  holderName?: string | null;
}

export interface ActivationKeyNode {
  id: string;
  nodeCode: string;
  deviceName?: string | null;
  enrolledAt: string;
  lastSeenAt?: string | null;
  appVersion?: string | null;
  status: string;
  revokedAt?: string | null;
}

export function listActivationKeys(): Promise<ActivationKeySummary[]> {
  return serverGet<ActivationKeySummary[]>("/api/admin/activation/keys");
}

export interface SchoolSummary {
  id: string;
  cue: string;
  code: string;
  name: string;
  localityId: string;
  annex?: number | null;
  status: string;
}

export function listSchools(): Promise<SchoolSummary[]> {
  return serverGet<SchoolSummary[]>("/api/admin/schools");
}

export interface SchoolStatsRow {
  cue: string;
  schoolName: string | null;
  attemptCount: number | string;
  averageScorePercent: number | string;
  liveSessionCount?: number;
  liveJoinedCount?: number;
  liveInProgressCount?: number;
  liveSubmittedCount?: number;
}

export interface LiveSessionSummary {
  sessionId: string;
  cue: string;
  schoolYear: string | null;
  rosterSectionId: string | null;
  examVersionId: string | null;
  status: string;
  joinedCount: number;
  inProgressCount: number;
  submittedCount: number;
  closedOrForcedCount: number;
  startedAt: string;
  lastActivityAt: string | null;
  lastHeartbeatAt: string | null;
  signalStatus: string;
  appVersion: string | null;
  heartbeatStaleAfterSeconds: number;
}

export function listLiveSessions(): Promise<LiveSessionSummary[]> {
  return serverGet<LiveSessionSummary[]>("/api/admin/live-sessions");
}

export interface SchoolYearOption {
  value: string;
  label: string;
}

export function listSchoolYears(): Promise<SchoolYearOption[]> {
  return serverGet<SchoolYearOption[]>("/api/stats/school-years");
}

export interface ReceivedSyncAttempt {
  attemptId: string;
  receivedAt: string;
  receiptStatus: string;
  durableReceivedAt: string | null;
  processingStatus: string;
  processingUpdatedAt: string | null;
  rollupStatus: string;
  nodeId: string | null;
  cue: string | null;
  schoolYear: string | null;
  rosterSectionId: string | null;
  examVersionId: string | null;
  gradingStatus: string;
  gradingReason: string | null;
  attributionStatus: string;
  attributionReason: string | null;
}

export interface ReceivedSyncPage {
  page: number;
  pageSize: number;
  totalCount: number;
  inboxProcessing: {
    pending: number;
    failed: number;
    latestDurableReceivedAt: string | null;
    nextRetryAt: string | null;
  };
  items: ReceivedSyncAttempt[];
}

export function listReceivedSyncAttempts(page = 1): Promise<ReceivedSyncPage> {
  return serverGet<ReceivedSyncPage>(`/api/admin/sync/received?page=${page}&pageSize=50`);
}

export function listSchoolStats(schoolYear?: string, course?: string): Promise<SchoolStatsRow[]> {
  const params = new URLSearchParams();
  if (schoolYear) params.set("schoolYear", schoolYear);
  if (course) params.set("course", course);
  const query = params.toString();
  return serverGet<SchoolStatsRow[]>(`/api/stats/schools${query ? `?${query}` : ""}`);
}

export type StatsMetric<T> = { value: T | null; status: "available" | "suppressed" | "unavailable" };
export interface StatsOption { value: string; label: string }
export interface StatsPage<T> { page: number; pageSize: number; totalCount: number; items: T[] }
export interface StatsSummary {
  publishedExams: StatsMetric<number>;
  schoolsWithResults: StatsMetric<number>;
  gradedAttempts: StatsMetric<number>;
  freshSessions: StatsMetric<number>;
  pendingAttribution: StatsMetric<number> | null;
  generatedAt: string;
  latestRollupUpdatedAt: string | null;
}
export interface StatsAggregateRow {
  key: string;
  label: string;
  attemptCount: StatsMetric<number>;
  weightedScorePercent: StatsMetric<number>;
}
export interface StatsAggregate {
  dataState: "available" | "no_results";
  generatedAt: string;
  latestRollupUpdatedAt: string | null;
  dimension: string;
  filters: Record<string, string | null>;
  rows: StatsAggregateRow[];
  page: number;
  pageSize: number;
  totalCount: number;
}
export interface SchoolStatsListRow {
  cue: string;
  schoolName: string;
  annex: number | null;
  localityId: string | null;
  locality: string | null;
  departmentId: string | null;
  department: string | null;
  attemptCount: StatsMetric<number>;
  averageScorePercent: StatsMetric<number>;
  freshSession: boolean;
  latestRollupUpdatedAt: string | null;
}

export function getStatsSummary(): Promise<StatsSummary> {
  return serverGet<StatsSummary>("/api/stats/summary");
}

export function listStatsCatalog(dimension: string, query?: string, page = 1, pageSize = 50): Promise<StatsPage<StatsOption>> {
  const params = new URLSearchParams({ dimension, page: String(page), pageSize: String(pageSize) });
  if (query) params.set("query", query);
  return serverGet<StatsPage<StatsOption>>(`/api/stats/catalogs?${params.toString()}`);
}

export function getStatsAggregate(input: {
  groupBy: string;
  localityId?: string;
  departmentId?: string;
  course?: string;
  subject?: string;
  schoolYear?: string;
  school?: string;
  version?: string;
  page?: number;
  pageSize?: number;
}): Promise<StatsAggregate> {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(input)) if (value !== undefined) params.set(key, String(value));
  return serverGet<StatsAggregate>(`/api/stats/aggregate?${params.toString()}`);
}

export function listPagedSchoolStats(page = 1, pageSize = 50, schoolYear?: string, course?: string): Promise<StatsPage<SchoolStatsListRow>> {
  const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  if (schoolYear) params.set("schoolYear", schoolYear);
  if (course) params.set("course", course);
  return serverGet<StatsPage<SchoolStatsListRow>>(`/api/stats/school-list?${params.toString()}`);
}

export function getSchoolStatsDetail(cue: string, schoolYear?: string, course?: string): Promise<SchoolStatsDetail> {
  const params = new URLSearchParams({ cue });
  if (schoolYear) params.set("schoolYear", schoolYear);
  if (course) params.set("course", course);
  return serverGet<SchoolStatsDetail>(`/api/stats/school?${params.toString()}`);
}

export interface SchoolStatsDetail {
  cue: string;
  schoolName: string | null;
  localityId: string | null;
  locality: string | null;
  departmentId: string | null;
  department: string | null;
  attemptCount: number | string | StatsMetric<number>;
  averageScorePercent: number | string | StatsMetric<number>;
  latestRollupUpdatedAt: string | null;
}

export interface UserSummary {
  id: string;
  email: string;
  fullName: string;
  status: string;
  cues: string[];
  roleCodes: string[];
}

export function listUsers(): Promise<UserSummary[]> {
  return serverGet<UserSummary[]>("/api/admin/users");
}

export interface RoleSummary {
  id: string;
  code: string;
  name: string;
  description?: string | null;
}

export function listRoles(): Promise<RoleSummary[]> {
  return serverGet<RoleSummary[]>("/api/admin/roles");
}
