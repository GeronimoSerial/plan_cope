import type { ApiErrorPayload } from "../shared/api-types";
import type { StartAttemptResponse, SubmitAttemptResponse } from "./types";

export class StudentApi {
  constructor(private readonly baseUrl = window.location.origin) {}

  startAttempt(sessionIdOrAccessCode: string): Promise<StartAttemptResponse> {
    return this.request<StartAttemptResponse>(`/api/sessions/${encodeURIComponent(sessionIdOrAccessCode)}/attempts`, {
      method: "POST"
    });
  }

  async saveAnswers(attemptId: string, answers: Array<{ blockId: string; answer: string | null }>): Promise<void> {
    await this.request<void>(`/api/attempts/${encodeURIComponent(attemptId)}/answers`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ answers })
    });
  }

  submitAttempt(attemptId: string): Promise<SubmitAttemptResponse> {
    return this.request<SubmitAttemptResponse>(`/api/attempts/${encodeURIComponent(attemptId)}/submit`, {
      method: "POST"
    });
  }

  private async request<T>(path: string, init: RequestInit): Promise<T> {
    const response = await fetch(`${this.baseUrl}${path}`, init);

    if (!response.ok) {
      throw new Error(await readApiError(response));
    }

    if (response.status === 204) {
      return undefined as T;
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
