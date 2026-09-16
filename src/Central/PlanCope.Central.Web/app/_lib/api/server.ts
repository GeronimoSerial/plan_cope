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

class NoInstallerPublishedError extends Error {
  constructor() {
    super("No installer published yet.");
    this.name = "NoInstallerPublishedError";
  }
}

export function isSessionExpired(error: unknown): boolean {
  return error instanceof SessionExpiredError;
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
}

export function listActivationKeys(): Promise<ActivationKeySummary[]> {
  return serverGet<ActivationKeySummary[]>("/api/admin/activation/keys");
}

export interface RegisteredNodeSummary {
  id: string;
  nodeCode: string;
  cue: string;
  deviceName?: string | null;
  enrolledAt: string;
  lastSeenAt?: string | null;
  revokedAt?: string | null;
  schoolName?: string | null;
}

export function listRegisteredNodes(): Promise<RegisteredNodeSummary[]> {
  return serverGet<RegisteredNodeSummary[]>("/api/admin/activation/nodes");
}

export interface SchoolStatsRow {
  cue: string;
  attemptCount: number | string;
  averageScorePercent: number | string;
}

export function listSchoolStats(schoolYear?: string, course?: string): Promise<SchoolStatsRow[]> {
  const params = new URLSearchParams();
  if (schoolYear) params.set("schoolYear", schoolYear);
  if (course) params.set("course", course);
  const query = params.toString();
  return serverGet<SchoolStatsRow[]>(`/api/stats/schools${query ? `?${query}` : ""}`);
}

export interface UnassignedExamVersion {
  examVersionId: string;
  examCode: string;
  versionNumber: number;
  publishedAt?: string | null;
}

export function listUnassignedGradingPolicies(): Promise<UnassignedExamVersion[]> {
  return serverGet<UnassignedExamVersion[]>("/api/admin/grading-policies/unassigned");
}
