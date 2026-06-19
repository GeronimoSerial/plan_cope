import "server-only";
import { centralFetch } from "../server/central-server";
import { getAccessToken } from "../server/session";
import { extractApiError } from "../json";
import type { ExamSummary, ExamVersion } from "../contracts";

class SessionExpiredError extends Error {
  constructor() {
    super("Sesion expirada.");
    this.name = "SessionExpiredError";
  }
}

export function isSessionExpired(error: unknown): boolean {
  return error instanceof SessionExpiredError;
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
