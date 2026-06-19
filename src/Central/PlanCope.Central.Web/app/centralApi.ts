import type { BlockType, ExamSummary, ExamVersion, PublishRequest } from "./types";

export class CentralApi {
  constructor(
    private readonly baseUrl: string,
    private readonly token: string
  ) {}

  async login(username: string, password: string): Promise<string> {
    const response = await fetch(`${this.baseUrl}/api/auth/login`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ username, password })
    });
    const payload = await readJson<{ accessToken?: string; token?: string }>(response);
    return payload.accessToken ?? payload.token ?? "";
  }

  listExams(): Promise<ExamSummary[]> {
    return this.get("/api/exams");
  }

  createExam(request: { code: string; title: string; description?: string; level?: string; area?: string; subject?: string }): Promise<ExamSummary> {
    return this.post("/api/exams", request);
  }

  listVersions(examId: string): Promise<ExamVersion[]> {
    return this.get(`/api/exams/${encodeURIComponent(examId)}/versions`);
  }

  createVersion(examId: string, metadata: Record<string, unknown>): Promise<ExamVersion> {
    return this.post(`/api/exams/${encodeURIComponent(examId)}/versions`, { schemaVersion: 1, metadata });
  }

  getVersion(versionId: string): Promise<ExamVersion> {
    return this.get(`/api/exams/versions/${encodeURIComponent(versionId)}`);
  }

  upsertBlock(versionId: string, request: { orderIndex: number; blockType: BlockType; title?: string; description?: string; config: Record<string, unknown>; validation?: Record<string, unknown> | null }) {
    return this.put(`/api/exams/versions/${encodeURIComponent(versionId)}/blocks`, request);
  }

  publishVersion(versionId: string, request: PublishRequest) {
    return this.post(`/api/exams/versions/${encodeURIComponent(versionId)}/publish`, request);
  }

  private get<T>(path: string): Promise<T> {
    return this.request<T>(path, { method: "GET" });
  }

  private post<T>(path: string, body: unknown): Promise<T> {
    return this.request<T>(path, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body)
    });
  }

  private put<T>(path: string, body: unknown): Promise<T> {
    return this.request<T>(path, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body)
    });
  }

  private async request<T>(path: string, init: RequestInit): Promise<T> {
    const headers = new Headers(init.headers);
    if (this.token) {
      headers.set("Authorization", `Bearer ${this.token}`);
    }

    const response = await fetch(`${this.baseUrl}${path}`, { ...init, headers });
    return readJson<T>(response);
  }
}

async function readJson<T>(response: Response): Promise<T> {
  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `Central API respondio con estado ${response.status}.`);
  }

  return response.json() as Promise<T>;
}
