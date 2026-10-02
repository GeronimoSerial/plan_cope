import { afterEach, describe, expect, it, vi } from "vitest";
import { StudentApi } from "./studentApi";

function jsonResponse(value: unknown): Response {
  return {
    ok: true,
    status: 200,
    json: async () => value
  } as Response;
}

describe("StudentApi exam resume contract", () => {
  afterEach(() => vi.unstubAllGlobals());

  it("sends the pre-stored resume credential with an idempotent attempt start", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ attempt: {}, blocks: [], resumeCredential: "opaque", credentialExpiresAt: "later" }));
    vi.stubGlobal("fetch", fetchMock);

    await new StudentApi("http://local.test").startAttempt("ABC-123", "resolution", "opaque");

    expect(fetchMock).toHaveBeenCalledWith("http://local.test/api/sessions/ABC-123/attempts", expect.objectContaining({
      method: "POST",
      body: JSON.stringify({ resolutionToken: "resolution", resumeCredential: "opaque" })
    }));
  });

  it("discovers an attempt whose start response was lost, then restores it with the same credential", async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse({ attemptId: "attempt-1" }))
      .mockResolvedValueOnce(jsonResponse({ attempt: { id: "attempt-1" }, blocks: [], answers: [], sessionStatus: "active" }));
    vi.stubGlobal("fetch", fetchMock);

    await new StudentApi("http://local.test").restoreAttempt("", "ABC-123", "opaque-credential");

    expect(fetchMock).toHaveBeenNthCalledWith(1, "http://local.test/api/sessions/ABC-123/attempts/restore", expect.objectContaining({
      headers: { Authorization: "Bearer opaque-credential" }
    }));
    expect(fetchMock).toHaveBeenNthCalledWith(2, "http://local.test/api/attempts/attempt-1/restore", expect.objectContaining({
      headers: { Authorization: "Bearer opaque-credential" }
    }));
  });

  it("sends answer revisions and requires the credential for mutation", async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, status: 204 });
    vi.stubGlobal("fetch", fetchMock);

    await new StudentApi("http://local.test").saveAnswers("attempt-1", "opaque-credential", 7, [{ blockId: "block-1", answer: "answer" }]);

    expect(fetchMock).toHaveBeenCalledWith("http://local.test/api/attempts/attempt-1/answers", expect.objectContaining({
      headers: { "Content-Type": "application/json", Authorization: "Bearer opaque-credential" },
      body: JSON.stringify({ revision: 7, answers: [{ blockId: "block-1", answer: "answer" }] })
    }));
  });
});
