// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { useStudentExam as useExam } from "./useStudentExam";

(globalThis as typeof globalThis & { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const STORAGE_KEY = "plancope.student.exam.v1";
const REVOCATION_KEY = "plancope.student.exam.revocation.v1";

function ExamProbe() {
  const exam = useExam();
  return (
    <section>
      <output data-testid="attempt">{exam.attemptId}</output>
      <output data-testid="status">{exam.status}</output>
      <textarea aria-label="Respuesta" value={exam.answers["block-1"] ?? ""} readOnly />
      <button onClick={exam.resetExam}>Reset</button>
    </section>
  );
}

async function tick(milliseconds = 0) {
  await act(async () => new Promise(resolve => setTimeout(resolve, milliseconds)));
}

describe("useStudentExam pending answer restoration", () => {
  let root: Root | undefined;
  let container: HTMLDivElement | undefined;

  afterEach(async () => {
    if (root) await act(async () => root?.unmount());
    container?.remove();
    root = undefined;
    container = undefined;
    window.sessionStorage.clear();
    vi.unstubAllGlobals();
  });

  it("shows a pending answer immediately offline and saves it once connection returns", async () => {
    window.sessionStorage.setItem(STORAGE_KEY, JSON.stringify({
      attemptId: "attempt-1",
      deliverySessionId: "session-1",
      sessionCode: "ACTIVE1",
      credential: "resume-secret",
      expiresAt: "later",
      nextRevision: 4,
      pending: [{ blockId: "block-1", answer: "respuesta local", revision: 4 }]
    }));

    let answerCalls = 0;
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.endsWith("/api/attempts/attempt-1/restore")) {
        return new Response(JSON.stringify({
          attempt: { id: "attempt-1", deliverySessionId: "session-1", status: "in_progress" },
          blocks: [],
          answers: [],
          sessionStatus: "active"
        }), { status: 200, headers: { "Content-Type": "application/json" } });
      }
      if (url.endsWith("/api/attempts/attempt-1/answers")) {
        answerCalls++;
        if (answerCalls === 1) throw new TypeError("Failed to fetch");
        return new Response(null, { status: 204 });
      }
      if (url.endsWith("/api/sessions/ACTIVE1")) {
        return new Response(JSON.stringify({ status: "active" }), { status: 200, headers: { "Content-Type": "application/json" } });
      }
      throw new Error(`Unexpected Local API request: ${url}`);
    });
    vi.stubGlobal("fetch", fetchMock);

    container = document.createElement("div");
    document.body.append(container);
    root = createRoot(container);
    await act(async () => root?.render(<ExamProbe />));
    await tick(650);

    expect(container.querySelector('[data-testid="attempt"]')?.textContent).toBe("attempt-1");
    expect(container.querySelector("textarea")?.value).toBe("respuesta local");
    expect(container.querySelector('[data-testid="status"]')?.textContent).toBe("Pendiente por conexión.");
    expect(JSON.parse(window.sessionStorage.getItem(STORAGE_KEY)!).pending).toEqual([
      { blockId: "block-1", answer: "respuesta local", revision: 4 }
    ]);
    expect(fetchMock.mock.calls.filter(([input]) => String(input).endsWith("/attempts")).length).toBe(0);

    await act(async () => {
      window.dispatchEvent(new Event("online"));
      await new Promise(resolve => setTimeout(resolve, 0));
    });

    expect(answerCalls).toBe(2);
    expect(JSON.parse(window.sessionStorage.getItem(STORAGE_KEY)!).pending).toEqual([]);
  });

  it("clears the exam immediately and retries a minimal revocation tombstone after reload and reconnect", async () => {
    window.sessionStorage.setItem(STORAGE_KEY, JSON.stringify({
      attemptId: "attempt-reset",
      deliverySessionId: "session-1",
      sessionCode: "ACTIVE1",
      credential: "credential-to-revoke",
      expiresAt: "later",
      nextRevision: 2,
      pending: [{ blockId: "block-1", answer: "private answer", revision: 2 }]
    }));

    let online = false;
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.endsWith("/api/attempts/attempt-reset/restore")) {
        return new Response(JSON.stringify({
          attempt: { id: "attempt-reset", deliverySessionId: "session-1", status: "in_progress" },
          blocks: [],
          answers: [{ blockId: "block-1", answer: "server answer", revision: 1 }],
          sessionStatus: "active"
        }), { status: 200, headers: { "Content-Type": "application/json" } });
      }
      if (url.endsWith("/api/attempts/attempt-reset/resume-credential/revoke")) {
        if (!online) throw new TypeError("Failed to fetch");
        return new Response(null, { status: 204 });
      }
      throw new Error(`Unexpected Local API request: ${url}`);
    });
    vi.stubGlobal("fetch", fetchMock);

    container = document.createElement("div");
    document.body.append(container);
    root = createRoot(container);
    await act(async () => root?.render(<ExamProbe />));
    await tick();
    expect(container.querySelector("textarea")?.value).toBe("private answer");

    await act(async () => container?.querySelector("button")?.click());
    expect(container.querySelector('[data-testid="attempt"]')?.textContent).toBe("");
    expect(container.querySelector("textarea")?.value).toBe("");
    expect(window.sessionStorage.getItem(STORAGE_KEY)).toBeNull();
    expect(JSON.parse(window.sessionStorage.getItem(REVOCATION_KEY)!)).toEqual({
      attemptId: "attempt-reset",
      sessionIdOrCode: "session-1",
      credential: "credential-to-revoke"
    });

    await act(async () => root?.unmount());
    root = createRoot(container);
    await act(async () => root?.render(<ExamProbe />));
    await tick();
    expect(fetchMock.mock.calls.filter(([input]) => String(input).endsWith("/restore"))).toHaveLength(1);

    online = true;
    await act(async () => {
      window.dispatchEvent(new Event("online"));
      await new Promise(resolve => setTimeout(resolve, 0));
    });
    expect(window.sessionStorage.getItem(REVOCATION_KEY)).toBeNull();
    expect(fetchMock.mock.calls.filter(([input]) => String(input).endsWith("/resume-credential/revoke"))).toHaveLength(3);
    expect(container.querySelector("textarea")?.value).toBe("");
  });
});
