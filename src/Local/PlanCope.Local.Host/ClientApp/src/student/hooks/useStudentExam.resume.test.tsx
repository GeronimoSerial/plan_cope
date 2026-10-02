// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { useStudentExam as useExam } from "./useStudentExam";

(globalThis as typeof globalThis & { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const STORAGE_KEY = "plancope.student.exam.v1";

function ExamProbe() {
  const exam = useExam();
  return (
    <section>
      <output data-testid="attempt">{exam.attemptId}</output>
      <output data-testid="status">{exam.status}</output>
      <output data-testid="recovery">{String(exam.recoveryRequired)}</output>
      <output data-testid="error">{exam.error}</output>
      <output data-testid="has-resolution">{String(Boolean(exam.resolution))}</output>
      <textarea aria-label="Respuesta" value={exam.answers["block-1"] ?? ""} readOnly />
      {exam.attemptId && <button onClick={() => exam.setAnswer("block-1", "respuesta nueva")}>edit</button>}
      <button onClick={() => exam.setDocument("12345678")}>document</button>
      <button onClick={() => void exam.resolveStudent()}>resolve</button>
      <button onClick={() => void exam.startAttempt()}>start</button>
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

    expect(container.querySelector('[data-testid="attempt"]')?.textContent, container.querySelector('[data-testid="error"]')?.textContent).toBe("attempt-1");
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

  it("keeps pending answers after an expired credential and recovers only after identity confirmation", async () => {
    window.sessionStorage.setItem(STORAGE_KEY, JSON.stringify({
      attemptId: "attempt-1",
      deliverySessionId: "session-1",
      sessionCode: "ACTIVE1",
      credential: "expired-secret",
      expiresAt: "expired",
      nextRevision: 4,
      pending: [{ blockId: "block-1", answer: "respuesta offline", revision: 4 }]
    }));

    let answerCalls = 0;
    let recoveredCredential = "";
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      if (url.endsWith("/api/attempts/attempt-1/restore") && init?.headers && (init.headers as Record<string, string>).Authorization !== `Bearer ${recoveredCredential}`) {
        return new Response(JSON.stringify({ error: "expired" }), { status: 401, headers: { "Content-Type": "application/json" } });
      }
      if (url.endsWith("/api/sessions/ACTIVE1")) {
        return new Response(JSON.stringify({ status: "active", rosterSnapshotId: "snapshot-1", rosterSectionId: "section-1" }), { status: 200, headers: { "Content-Type": "application/json" } });
      }
      if (url.endsWith("/student-resolution")) {
        return new Response(JSON.stringify({ resolutionToken: "verified-identity", student: { firstName: "Ana", lastName: "Pérez", displayName: "Pérez, Ana", maskedDocument: "**.***.5678" } }), { status: 200, headers: { "Content-Type": "application/json" } });
      }
      if (url.endsWith("/api/sessions/ACTIVE1/attempts")) {
        const body = JSON.parse(String(init?.body));
        expect(body).toMatchObject({ resolutionToken: "verified-identity", recoverAttemptId: "attempt-1" });
        recoveredCredential = body.resumeCredential;
        return new Response(JSON.stringify({ attempt: { id: "attempt-1", deliverySessionId: "session-1", status: "in_progress" }, blocks: [], resumeCredential: recoveredCredential, credentialExpiresAt: "later" }), { status: 201, headers: { "Content-Type": "application/json" } });
      }
      if (url.endsWith("/api/attempts/attempt-1/restore") && (init?.headers as Record<string, string>).Authorization === `Bearer ${recoveredCredential}`) {
        return new Response(JSON.stringify({
          attempt: { id: "attempt-1", deliverySessionId: "session-1", status: "in_progress" },
          blocks: [],
          answers: [{ blockId: "block-1", answer: "respuesta confirmada" }],
          sessionStatus: "active"
        }), { status: 200, headers: { "Content-Type": "application/json" } });
      }
      if (url.endsWith("/api/attempts/attempt-1/answers")) {
        answerCalls++;
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

    expect(container.querySelector('[data-testid="recovery"]')?.textContent).toBe("true");
    expect(container.querySelector("textarea")?.value).toBe("");
    expect(JSON.parse(window.sessionStorage.getItem(STORAGE_KEY)!).pending).toEqual([
      { blockId: "block-1", answer: "respuesta offline", revision: 4 }
    ]);
    expect(JSON.parse(window.sessionStorage.getItem(STORAGE_KEY)!).credential).toBe("");

    await act(async () => {
      (container?.querySelectorAll("button")[0] as HTMLButtonElement).click();
      await new Promise(resolve => setTimeout(resolve, 0));
    });
    await act(async () => {
      (container?.querySelectorAll("button")[1] as HTMLButtonElement).click();
      await new Promise(resolve => setTimeout(resolve, 0));
    });
    await act(async () => {
      (container?.querySelectorAll("button")[2] as HTMLButtonElement).click();
      await new Promise(resolve => setTimeout(resolve, 50));
    });

    const requestUrls = fetchMock.mock.calls.map(([input]) => String(input));
    expect(requestUrls.some(url => url.endsWith("/api/sessions/ACTIVE1/attempts")), requestUrls.join("\n")).toBe(true);
    expect(container.querySelector('[data-testid="has-resolution"]')?.textContent, requestUrls.join("\n")).toBe("true");
    expect(container.querySelector('[data-testid="attempt"]')?.textContent, container.querySelector('[data-testid="error"]')?.textContent).toBe("attempt-1");
    expect(container.querySelector('[data-testid="recovery"]')?.textContent).toBe("false");
    expect(container.querySelector("textarea")?.value).toBe("respuesta offline");
    await tick(550);
    expect(answerCalls).toBe(1);
    expect(JSON.parse(window.sessionStorage.getItem(STORAGE_KEY)!).pending).toEqual([]);
    expect(fetchMock.mock.calls.some(([input]) => String(input).endsWith("/attempts") && JSON.parse(String(fetchMock.mock.calls.find(([candidate]) => String(candidate).endsWith("/attempts"))?.[1]?.body)).recoverAttemptId === "attempt-1")).toBe(true);
  });

  it("keeps edits and requests identity again when an active autosave receives 401", async () => {
    window.sessionStorage.setItem(STORAGE_KEY, JSON.stringify({
      attemptId: "attempt-1",
      deliverySessionId: "session-1",
      sessionCode: "ACTIVE1",
      credential: "valid-at-start",
      expiresAt: "later",
      nextRevision: 0,
      pending: []
    }));
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.endsWith("/api/attempts/attempt-1/restore")) {
        return new Response(JSON.stringify({
          attempt: { id: "attempt-1", deliverySessionId: "session-1", status: "in_progress", studentFirstName: "Ana", studentLastName: "Pérez" },
          blocks: [], answers: [], sessionStatus: "active"
        }), { status: 200, headers: { "Content-Type": "application/json" } });
      }
      if (url.endsWith("/api/sessions/ACTIVE1"))
        return new Response(JSON.stringify({ status: "active" }), { status: 200, headers: { "Content-Type": "application/json" } });
      if (url.endsWith("/api/attempts/attempt-1/answers"))
        return new Response(JSON.stringify({ kind: "resume_credential_invalid", error: "expired" }), { status: 401, headers: { "Content-Type": "application/json" } });
      throw new Error(`Unexpected Local API request: ${url}`);
    });
    vi.stubGlobal("fetch", fetchMock);

    container = document.createElement("div");
    document.body.append(container);
    root = createRoot(container);
    await act(async () => root?.render(<ExamProbe />));
    await tick();
    expect(container.querySelector('[data-testid="attempt"]')?.textContent).toBe("attempt-1");

    await act(async () => {
      (container?.querySelectorAll("button")[0] as HTMLButtonElement).click();
      await new Promise(resolve => setTimeout(resolve, 550));
    });

    expect(container.querySelector('[data-testid="attempt"]')?.textContent).toBe("");
    expect(container.querySelector('[data-testid="recovery"]')?.textContent).toBe("true");
    expect(JSON.parse(window.sessionStorage.getItem(STORAGE_KEY)!).credential).toBe("");
    expect(JSON.parse(window.sessionStorage.getItem(STORAGE_KEY)!).pending).toEqual([
      { blockId: "block-1", answer: "respuesta nueva", revision: 1 }
    ]);
  });
});
