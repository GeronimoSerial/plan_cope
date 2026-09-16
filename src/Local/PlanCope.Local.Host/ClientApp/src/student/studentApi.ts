import type { ApiErrorPayload } from "../shared/api-types";
import type { ResolveStudentResponse, StartAttemptResponse, SubmitAttemptResponse } from "./types";

export class StudentNotFoundError extends Error {
  constructor(message: string, readonly hint: string) {
    super(message);
    this.name = "StudentNotFoundError";
  }
}

export class StudentApi {
  constructor(private readonly baseUrl = window.location.origin) {}

  resolveStudent(sessionIdOrAccessCode: string, document: string): Promise<ResolveStudentResponse> {
    return this.request<ResolveStudentResponse>(
      `/api/sessions/${encodeURIComponent(sessionIdOrAccessCode)}/student-resolution`,
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ document })
      },
      { studentNotFound: true }
    );
  }

  startAttempt(sessionIdOrAccessCode: string, resolutionToken?: string): Promise<StartAttemptResponse> {
    return this.request<StartAttemptResponse>(`/api/sessions/${encodeURIComponent(sessionIdOrAccessCode)}/attempts`, {
      method: "POST",
      ...(resolutionToken
        ? {
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ resolutionToken })
          }
        : {})
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

  private async request<T>(path: string, init: RequestInit, options?: { studentNotFound?: boolean }): Promise<T> {
    const response = await fetch(`${this.baseUrl}${path}`, init);

    if (!response.ok) {
      if (options?.studentNotFound && response.status === 404) {
        const studentNotFound = await readStudentNotFound(response.clone());
        if (studentNotFound) {
          throw studentNotFound;
        }
      }

      throw new Error(await readApiError(response));
    }

    if (response.status === 204) {
      return undefined as T;
    }

    return response.json() as Promise<T>;
  }
}

async function readStudentNotFound(response: Response): Promise<StudentNotFoundError | null> {
  try {
    const payload = (await response.json()) as { kind?: string; message?: string; hint?: string };
    if (payload.kind === "not_found") {
      return new StudentNotFoundError(payload.message ?? "", payload.hint ?? "");
    }
  } catch {
    return null;
  }

  return null;
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
