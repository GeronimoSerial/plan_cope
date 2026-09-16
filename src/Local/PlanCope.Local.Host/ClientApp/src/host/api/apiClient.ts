import type {
  CreateSessionRequest,
  LocalSession,
  RosterResponse,
  SessionProgress
} from "../types";
import type { ApiErrorPayload, LocalExam } from "../../shared/api-types";

export type SyncStatusDto = {
  healthy: boolean;
  offline: boolean;
  lastError: string | null;
  lastPullAt: string | null;
  lastPushAt: string | null;
  nextAttempt: string | null;
};

export type CourseStatDto = { course: string; attemptCount: number | string; averageScorePercent: number | string };
export type BlockStatDto = { blockId: string; correctCount: number; partialCount: number; incorrectCount: number; blankCount: number; ungradableCount: number };
export type ExamStatDto = { examVersionId: string; examCode: string; versionNumber: number; attemptCount: number | string; averageScorePercent: number | string; blocks: BlockStatDto[] };

export class ApiClient {
  constructor(private readonly baseUrl: string) {}

  getExams(signal?: AbortSignal): Promise<LocalExam[]> {
    return this.get<LocalExam[]>("/api/exams/", signal);
  }

  pullExams(signal?: AbortSignal): Promise<unknown> {
    return this.post<unknown>("/api/sync/pull-exams", {}, signal);
  }

  getSyncStatus(signal?: AbortSignal): Promise<SyncStatusDto> {
    return this.get<SyncStatusDto>("/api/sync/status", signal);
  }

  getLatestRoster(cue: string, signal?: AbortSignal): Promise<RosterResponse> {
    const query = new URLSearchParams({ cue });
    return this.get<RosterResponse>(`/api/rosters/latest?${query.toString()}`, signal);
  }

  createSession(request: CreateSessionRequest, signal?: AbortSignal): Promise<LocalSession> {
    return this.post<LocalSession>("/api/sessions/", request, signal);
  }

  getActiveSessions(signal?: AbortSignal): Promise<LocalSession[]> {
    return this.get<LocalSession[]>("/api/sessions/active", signal);
  }

  getSession(idOrAccessCode: string, signal?: AbortSignal): Promise<LocalSession> {
    return this.get<LocalSession>(`/api/sessions/${encodeURIComponent(idOrAccessCode)}`, signal);
  }

  getSessionProgress(accessCode: string, signal?: AbortSignal): Promise<SessionProgress> {
    return this.get<SessionProgress>(`/api/sessions/${encodeURIComponent(accessCode)}/progress`, signal);
  }

  getCourseStats(cue: string, schoolYear: string | undefined, signal?: AbortSignal): Promise<CourseStatDto[]> {
    const query = new URLSearchParams({ cue });
    if (schoolYear) query.set("schoolYear", schoolYear);
    return this.get<CourseStatDto[]>(`/api/stats/course?${query.toString()}`, signal);
  }

  getExamStats(cue: string, schoolYear: string | undefined, course: string | undefined, signal?: AbortSignal): Promise<ExamStatDto[]> {
    const query = new URLSearchParams({ cue });
    if (schoolYear) query.set("schoolYear", schoolYear);
    if (course) query.set("course", course);
    return this.get<ExamStatDto[]>(`/api/stats/exam?${query.toString()}`, signal);
  }

  getStatsExportCsvUrl(cue: string, schoolYear: string | undefined): string {
    const query = new URLSearchParams({ cue });
    if (schoolYear) query.set("schoolYear", schoolYear);
    return `${this.baseUrl}/api/stats/export.csv?${query.toString()}`;
  }

  private async get<T>(path: string, signal?: AbortSignal): Promise<T> {
    return this.request<T>(path, { method: "GET", signal });
  }

  private async post<T>(path: string, body: unknown, signal?: AbortSignal): Promise<T> {
    return this.request<T>(path, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
      signal
    });
  }

  private async request<T>(path: string, init: RequestInit): Promise<T> {
    const response = await fetch(`${this.baseUrl}${path}`, init);

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }

    return response.json() as Promise<T>;
  }
}

async function readApiError(response: Response): Promise<string> {
  try {
    const payload = (await response.json()) as ApiErrorPayload;
    if (payload.error) {
      return payload.error;
    }

    const firstValidationError = payload.errors ? Object.values(payload.errors).flat()[0] : undefined;
    if (firstValidationError) {
      return firstValidationError;
    }
  } catch {
    return `La API local respondio con estado ${response.status}.`;
  }

  return `La API local respondio con estado ${response.status}.`;
}
