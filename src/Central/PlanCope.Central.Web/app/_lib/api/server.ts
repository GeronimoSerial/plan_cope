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
