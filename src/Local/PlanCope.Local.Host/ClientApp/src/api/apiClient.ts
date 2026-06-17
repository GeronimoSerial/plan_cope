import type {
  ApiErrorPayload,
  CreateSessionRequest,
  LocalExam,
  LocalSession,
  SessionProgress
} from "../types";

export class ApiClient {
  constructor(private readonly baseUrl: string) {}

  getExams(signal?: AbortSignal): Promise<LocalExam[]> {
    return this.get<LocalExam[]>("/api/exams/", signal);
  }

  createSession(request: CreateSessionRequest, signal?: AbortSignal): Promise<LocalSession> {
    return this.post<LocalSession>("/api/sessions/", request, signal);
  }

  getSessionProgress(accessCode: string, signal?: AbortSignal): Promise<SessionProgress> {
    return this.get<SessionProgress>(`/api/sessions/${encodeURIComponent(accessCode)}/progress`, signal);
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
