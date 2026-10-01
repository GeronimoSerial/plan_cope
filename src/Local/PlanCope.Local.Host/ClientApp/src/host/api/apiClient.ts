import type {
  CreateSessionRequest,
  LocalSession,
  RosterResponse,
  SessionProgress,
  LocalSchool,
  SessionHistoryPage
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

export type PullExamsStatus = "updated" | "up_to_date" | "error";

export type PullExamsResult = {
  status: PullExamsStatus;
  newExams: number;
  updatedExams: number;
  totalReceived: number;
  errorCode: string | null;
  message: string;
  lastPullAt: string | null;
};

export type CourseStatDto = { course: string; attemptCount: number | string; averageScorePercent: number | string };
export type BlockStatDto = { blockId: string; correctCount: number; partialCount: number; incorrectCount: number; blankCount: number; ungradableCount: number };
export type ExamStatDto = { examVersionId: string; examCode: string; versionNumber: number; attemptCount: number | string; averageScorePercent: number | string; blocks: BlockStatDto[] };
export type StatsFilterOptionsDto = { schoolYears: string[]; courses: string[]; exams: { examVersionId: string; examCode: string; versionNumber: number }[] };

export class ApiClient {
  constructor(private readonly baseUrl: string) {}

  getExams(signal?: AbortSignal): Promise<LocalExam[]> {
    return this.get<LocalExam[]>("/api/exams/", signal);
  }

  async pullExams(signal?: AbortSignal): Promise<PullExamsResult> {
    // The endpoint answers with the same JSON body for 200 and for the 409/502 error statuses,
    // so read the payload regardless of `response.ok` instead of throwing the body away.
    const response = await fetch(`${this.baseUrl}/api/sync/pull-exams`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: "{}",
      ...(isAbortSignal(signal) ? { signal } : {})
    });
    return (await response.json()) as PullExamsResult;
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

  getSessionHistory(filters: { schoolCode?: string; status?: string; page?: number; pageSize?: number }, signal?: AbortSignal): Promise<SessionHistoryPage> {
    const query = new URLSearchParams();
    for (const [key, value] of Object.entries(filters)) if (value !== undefined && value !== "") query.set(key, String(value));
    return this.get<SessionHistoryPage>(`/api/sessions/history?${query.toString()}`, signal);
  }

  getSchools(signal?: AbortSignal): Promise<LocalSchool[]> {
    return this.get<LocalSchool[]>("/api/schools", signal);
  }

  getSession(idOrAccessCode: string, signal?: AbortSignal): Promise<LocalSession> {
    return this.get<LocalSession>(`/api/sessions/${encodeURIComponent(idOrAccessCode)}`, signal);
  }

  getSessionProgress(accessCode: string, signal?: AbortSignal): Promise<SessionProgress> {
    return this.get<SessionProgress>(`/api/sessions/${encodeURIComponent(accessCode)}/progress`, signal);
  }

  updateSessionStatus(id: string, status: "active" | "paused" | "closed"): Promise<{ submitted: number; failed: number } | void> {
    return this.request(`/api/sessions/${encodeURIComponent(id)}/status`, {
      method: "PUT", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ status })
    });
  }

  addExtraStudent(sessionId: string, request: { document: string; firstName: string; lastName: string }): Promise<{ id: string }> {
    return this.post<{ id: string }>(`/api/sessions/${encodeURIComponent(sessionId)}/extra-students`, request);
  }

  async removeExtraStudent(sessionId: string, studentId: string): Promise<void> {
    const response = await fetch(`${this.baseUrl}/api/sessions/${encodeURIComponent(sessionId)}/extra-students/${encodeURIComponent(studentId)}`, { method: "DELETE" });
    if (!response.ok) throw new Error(await readApiError(response));
  }

  async discardSession(id: string): Promise<void> {
    const response = await fetch(`${this.baseUrl}/api/sessions/${encodeURIComponent(id)}`, { method: "DELETE" });
    if (!response.ok) throw new Error(await readApiError(response));
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

  async getStatsExportCsv(cue: string, schoolYear?: string): Promise<Blob> {
    const response = await fetch(this.getStatsExportCsvUrl(cue, schoolYear));
    if (!response.ok) throw new Error(await readApiError(response));
    return response.blob();
  }

  getStatsFilterOptions(cue: string, signal?: AbortSignal): Promise<StatsFilterOptionsDto> {
    const query = new URLSearchParams({ cue });
    return this.get<StatsFilterOptionsDto>(`/api/stats/filters?${query.toString()}`, signal);
  }

  getStatsHtmlReportUrl(cue: string, schoolYear?: string, course?: string, exam?: string): string {
    const query = new URLSearchParams({ cue });
    if (schoolYear) query.set("schoolYear", schoolYear);
    if (course) query.set("course", course);
    if (exam) query.set("exam", exam);
    return `${this.baseUrl}/api/stats/report.html?${query.toString()}`;
  }

  async getStatsHtmlReport(cue: string, schoolYear?: string, course?: string, exam?: string): Promise<Blob> {
    const response = await fetch(this.getStatsHtmlReportUrl(cue, schoolYear, course, exam));
    if (!response.ok) throw new Error(await readApiError(response));
    return response.blob();
  }

  private async get<T>(path: string, signal?: AbortSignal): Promise<T> {
    return this.request<T>(path, { method: "GET", ...(isAbortSignal(signal) ? { signal } : {}) });
  }

  private async post<T>(path: string, body: unknown, signal?: AbortSignal): Promise<T> {
    return this.request<T>(path, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
      ...(isAbortSignal(signal) ? { signal } : {})
    });
  }

  private async request<T>(path: string, init: RequestInit): Promise<T> {
    const response = await fetch(`${this.baseUrl}${path}`, init);

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }

    if (response.status === 204) return undefined as T;
    return response.json() as Promise<T>;
  }
}

function isAbortSignal(value: unknown): value is AbortSignal {
  return typeof AbortSignal !== "undefined" && value instanceof AbortSignal;
}

async function readApiError(response: Response): Promise<string> {
  try {
    const payload = (await response.json()) as ApiErrorPayload;
    const message = payload.error ?? payload.detail;
    if (message) return message;

    const firstValidationError = payload.errors ? Object.values(payload.errors).flat()[0] : undefined;
    if (firstValidationError) {
      return firstValidationError;
    }
  } catch {
    return `La API local respondio con estado ${response.status}.`;
  }

  return `La API local respondio con estado ${response.status}.`;
}
