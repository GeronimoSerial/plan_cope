import type { ApiErrorPayload } from "../shared/api-types";
import type { ResolveStudentResponse, RestoredAttemptResponse, StartAttemptResponse, SubmitAttemptResponse } from "./types";

export class StudentNotFoundError extends Error {
  constructor(message: string, readonly hint: string) {
    super(message);
    this.name = "StudentNotFoundError";
  }
}

export class ResumeCredentialError extends Error {
  constructor(readonly status: number, message: string) {
    super(message);
    this.name = "ResumeCredentialError";
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

  getSessionStatus(sessionIdOrAccessCode: string): Promise<{ status: string; rosterSnapshotId?: string | null; rosterSectionId?: string | null }> {
    return this.request<{ status: string; rosterSnapshotId?: string | null; rosterSectionId?: string | null }>(`/api/sessions/${encodeURIComponent(sessionIdOrAccessCode)}`, { method: "GET" });
  }

  startAttempt(sessionIdOrAccessCode: string, resolutionToken?: string, resumeCredential?: string, recoverAttemptId?: string, resumeProof?: string): Promise<StartAttemptResponse> {
    const body = JSON.stringify({ resolutionToken, resumeCredential, recoverAttemptId, resumeProof });
    return this.request<StartAttemptResponse>(`/api/sessions/${encodeURIComponent(sessionIdOrAccessCode)}/attempts`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body
    });
  }

  async restoreAttempt(attemptId: string, sessionCode: string, credential: string): Promise<RestoredAttemptResponse> {
    if (!attemptId) {
      const found = await this.request<{ attemptId: string }>(`/api/sessions/${encodeURIComponent(sessionCode)}/attempts/restore`, {
        method: "GET",
        headers: { Authorization: `Bearer ${credential}` }
      });
      attemptId = found.attemptId;
    }
    return this.request<RestoredAttemptResponse>(`/api/attempts/${encodeURIComponent(attemptId)}/restore`, {
      method: "GET",
      headers: { Authorization: `Bearer ${credential}` }
    });
  }

  async saveAnswers(attemptId: string, credential: string, revision: number, answers: Array<{ blockId: string; answer: unknown }>): Promise<void> {
    await this.request<void>(`/api/attempts/${encodeURIComponent(attemptId)}/answers`, {
      method: "PUT",
      headers: { "Content-Type": "application/json", Authorization: `Bearer ${credential}` },
      body: JSON.stringify({ revision, answers })
    });
  }

  submitAttempt(attemptId: string, credential: string): Promise<SubmitAttemptResponse> {
    return this.request<SubmitAttemptResponse>(`/api/attempts/${encodeURIComponent(attemptId)}/submit`, {
      method: "POST",
      headers: { Authorization: `Bearer ${credential}` }
    });
  }

  async revokeAttemptRecovery(attemptId: string, proof: string): Promise<void> {
    await this.request<void>(`/api/attempts/${encodeURIComponent(attemptId)}/recovery/revoke`, {
      method: "POST",
      headers: { Authorization: `Bearer ${proof}` }
    });
  }

  private async request<T>(path: string, init: RequestInit, options?: { studentNotFound?: boolean }): Promise<T> {
    const response = await fetch(`${this.baseUrl}${path}`, init);

    if (!response.ok) {
      if (response.status === 401 || response.status === 410) {
        throw new ResumeCredentialError(response.status, await readApiError(response));
      }
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
