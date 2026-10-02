// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { renderToStaticMarkup } from "react-dom/server";
import { afterEach, describe, expect, it, vi } from "vitest";
import { StatsWorkspace } from "./StatsWorkspace";

Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });

describe("StatsWorkspace", () => {
  let root: Root | undefined;
  let container: HTMLDivElement | undefined;

  afterEach(() => {
    if (root) act(() => root?.unmount());
    root = undefined;
    container?.remove();
    container = undefined;
    vi.restoreAllMocks();
    vi.useRealTimers();
    vi.unstubAllGlobals();
    delete (window as Window & { chrome?: unknown }).chrome;
  });

  it("renders report controls, known filter selectors, live copy, and a useful empty state", () => {
    const html = renderToStaticMarkup(<StatsWorkspace apiBaseUrl="http://localhost" cue="123456789" schoolYear="2026" onBack={() => undefined} />);

    expect(html).toContain("Año lectivo");
    expect(html).toContain('id="stats-school-year-filter"');
    expect(html).toContain('id="stats-course-filter"');
    expect(html).toContain("Generar informe HTML");
    expect(html).toContain("Los datos se actualizan cada 15 segundos");
    expect(html).toContain("Volver a sesiones");
    expect(html).toContain("Actualizar ahora");
    expect(html).toContain("Todavía no hay intentos entregados");
  });

  it("keeps every course grade available when the API has no section options", async () => {
    installStatsFilterFetch({ schoolYears: [], courses: ["5", "6º"], sections: [], exams: [] });
    await renderComponent();
    await flushEffects();

    const grade = container!.querySelector<HTMLSelectElement>("#stats-course-filter")!;
    expect([...grade.options].map(option => option.text)).toEqual(["Todos los grados", "5", "6°"]);
    const section = container!.querySelector<HTMLSelectElement>("#stats-section-filter")!;
    act(() => { grade.value = "5"; grade.dispatchEvent(new Event("change", { bubbles: true })); });
    expect([...section.options].map(option => option.text)).toEqual(["Todas las secciones"]);
  });

  it("unions course grades with section grades and only lists sections for the selected grade", async () => {
    installStatsFilterFetch({
      schoolYears: [], courses: ["5", "6", "7"],
      sections: [{ course: "6", division: "A", shift: "Mañana" }, { course: "7", division: "B", shift: null }], exams: []
    });
    await renderComponent();
    await flushEffects();

    const grade = container!.querySelector<HTMLSelectElement>("#stats-course-filter")!;
    const section = container!.querySelector<HTMLSelectElement>("#stats-section-filter")!;
    expect([...grade.options].map(option => option.text)).toEqual(["Todos los grados", "5", "6", "7"]);
    act(() => { grade.value = "5"; grade.dispatchEvent(new Event("change", { bubbles: true })); });
    expect([...section.options].map(option => option.text)).toEqual(["Todas las secciones"]);
    act(() => { grade.value = "6"; grade.dispatchEvent(new Event("change", { bubbles: true })); });
    expect([...section.options].map(option => option.text)).toEqual(["Todas las secciones", "A"]);
  });

  it("filters exams by the selected shift without entering a loading state or refetching", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      const body = url.includes("/api/schools?withAttempts=true")
        ? [{ code: "123456789", name: "Escuela Test", submittedAttemptCount: 2, lastSubmittedAt: "2026-09-30T10:12:00Z" }]
        : url.includes("/api/stats/filters?")
          ? { schoolYears: [], courses: ["6"], sections: [{ course: "6", division: "A", shift: "Mañana" }, { course: "6", division: "A", shift: "Tarde" }], exams: [] }
          : url.includes("/api/stats/course?")
            ? [{ course: "6", attemptCount: 2, averageScorePercent: 70 }]
            : url.includes("/api/stats/exam?")
              ? [
                { examVersionId: "morning", examCode: "MAT-M", courses: ["6"], sections: [{ course: "6", division: "A", shift: "Mañana" }], versionNumber: 1, attemptCount: 1, averageScorePercent: 75, blocks: [] },
                { examVersionId: "afternoon", examCode: "MAT-T", courses: ["6"], sections: [{ course: "6", division: "A", shift: "Tarde" }], versionNumber: 1, attemptCount: 1, averageScorePercent: 65, blocks: [] }
              ] : [];
      return { ok: true, json: async () => body };
    });
    vi.stubGlobal("fetch", fetchMock);
    await renderComponent();
    await flushEffects();

    const grade = container!.querySelector<HTMLSelectElement>("#stats-course-filter")!;
    const section = container!.querySelector<HTMLSelectElement>("#stats-section-filter")!;
    act(() => { grade.value = "6"; grade.dispatchEvent(new Event("change", { bubbles: true })); });
    await flushEffects();
    expect([...section.options].map(option => option.text)).toEqual(["Todas las secciones", "A · Mañana", "A · Tarde"]);

    const examCallsBeforeSectionChange = fetchMock.mock.calls.filter(([input]) => String(input).includes("/api/stats/exam?")).length;
    act(() => { section.value = "A\u001fMañana"; section.dispatchEvent(new Event("change", { bubbles: true })); });
    expect(container?.textContent).toContain("MAT-M");
    expect(container?.textContent).not.toContain("MAT-T");
    expect(container?.textContent).not.toContain("Actualizando estadísticas…");
    expect(fetchMock.mock.calls.filter(([input]) => String(input).includes("/api/stats/exam?")).length).toBe(examCallsBeforeSectionChange);

    act(() => { section.value = "A\u001fTarde"; section.dispatchEvent(new Event("change", { bubbles: true })); });
    expect(container?.textContent).toContain("MAT-T");
    expect(container?.textContent).not.toContain("MAT-M");
  });

  it("shows ordered question labels and prompts instead of block identifiers", async () => {
    const blockId = "1f37ed04-5275-4e47-90c0-14d5e18ff1ae";
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      const body = url.includes("/api/schools?withAttempts=true")
        ? [{ code: "123456789", name: "Escuela Test", submittedAttemptCount: 2, lastSubmittedAt: "2026-09-30T10:12:00Z" }]
        : url.includes("/api/stats/filters?")
        ? { schoolYears: ["2026"], courses: [], exams: [] }
        : url.includes("/api/stats/course?")
          ? [{ course: "6", attemptCount: 2, averageScorePercent: 75 }]
          : url.includes("/api/stats/exam?")
            ? [{ examVersionId: "exam-v1", examCode: "BIO", versionNumber: 1, attemptCount: 2, averageScorePercent: 75, blocks: [{ blockId, orderIndex: 2, title: "¿Cuál es la función principal de las raíces?", correctCount: 1, partialCount: 0, incorrectCount: 1, blankCount: 0, ungradableCount: 0 }] }]
            : [];
      return { ok: true, json: async () => body };
    });
    vi.stubGlobal("fetch", fetchMock);

    await renderComponent();

    expect(container?.querySelector(".stats-workspace-panel")).not.toBeNull();
    expect(container?.querySelector(".stats-block-table")).not.toBeNull();
    expect(container?.textContent).toContain("Pregunta 3");
    expect(container?.textContent).toContain("¿Cuál es la función principal de las raíces?");
    expect(container?.textContent).not.toContain(blockId);
  });

  it("uses the course and exam selectors as the only statistics filters", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      const body = url.includes("/api/schools?withAttempts=true")
        ? [{ code: "123456789", name: "Escuela Test", submittedAttemptCount: 2, lastSubmittedAt: "2026-09-30T10:12:00Z" }]
        : url.includes("/api/stats/filters?")
          ? { schoolYears: ["2026"], courses: ["6", "7"], exams: [{ examVersionId: "math-v1", examCode: "MAT-6", versionNumber: 1 }, { examVersionId: "bio-v1", examCode: "BIO-7", versionNumber: 1 }] }
          : url.includes("/api/stats/course?")
            ? [{ course: "6", attemptCount: 1, averageScorePercent: 80 }, { course: "7", attemptCount: 1, averageScorePercent: 60 }]
            : url.includes("/api/stats/exam?") && new URL(url).searchParams.get("course") === "7"
              ? [{ examVersionId: "bio-v1", examCode: "BIO-7", title: "Biología", courses: ["7"], versionNumber: 1, attemptCount: 1, averageScorePercent: 60, blocks: [] }]
              : [{ examVersionId: "math-v1", examCode: "MAT-6", title: "Matemática diagnóstico", courses: ["6"], versionNumber: 1, attemptCount: 1, averageScorePercent: 80, blocks: [] }, { examVersionId: "bio-v1", examCode: "BIO-7", title: "Biología", courses: ["7"], versionNumber: 1, attemptCount: 1, averageScorePercent: 60, blocks: [] }];
      return { ok: true, json: async () => body };
    });
    vi.stubGlobal("fetch", fetchMock);

    await renderComponent();
    const grade = container!.querySelector<HTMLSelectElement>("#stats-course-filter")!;
    act(() => { grade.value = "7"; grade.dispatchEvent(new Event("change", { bubbles: true })); });
    await flushEffects();
    expect([...container!.querySelectorAll("details summary")].map(summary => summary.textContent)).toEqual([expect.stringContaining("BIO-7")]);

    const exam = container!.querySelector<HTMLSelectElement>("#stats-exam-filter")!;
    act(() => { exam.value = "bio-v1"; exam.dispatchEvent(new Event("change", { bubbles: true })); });
    expect([...container!.querySelectorAll("details summary")].map(summary => summary.textContent)).toEqual([expect.stringContaining("BIO-7")]);
  });

  it("shows the school empty state and disables the HTML report when there are no submitted attempts", async () => {
    const fetchMock = vi.fn(async () => ({ ok: true, json: async () => [] }));
    vi.stubGlobal("fetch", fetchMock);

    await renderComponent();

    expect(container?.textContent).toContain("Todavía no hay exámenes entregados en este equipo.");
    expect(findButton("Generar informe HTML").disabled).toBe(true);
  });

  it("sends selected filters to the host and shows the report result", async () => {
    installFetchMock();
    const listeners = new Set<(event: MessageEvent) => void>();
    const sentMessages: Record<string, unknown>[] = [];
    Object.defineProperty(window, "chrome", {
      configurable: true,
      value: {
        webview: {
          addEventListener: (_type: string, listener: (event: MessageEvent) => void) => listeners.add(listener),
          removeEventListener: (_type: string, listener: (event: MessageEvent) => void) => listeners.delete(listener),
          postMessage: (message: Record<string, unknown>) => {
            sentMessages.push(message);
            queueMicrotask(() => listeners.forEach(listener => listener({ data: {
              type: "host:statsReportResult",
              requestId: message.requestId,
              success: true,
              path: "C:\\Users\\Teacher\\AppData\\Local\\PlanCope\\reports\\report.html",
              message: "Informe guardado y abierto en el navegador."
            } } as MessageEvent)));
          }
        }
      }
    });

    await renderComponent();
    const reportButton = findButton("Generar informe HTML");
    await act(async () => { reportButton.click(); await Promise.resolve(); await Promise.resolve(); });

    expect(sentMessages).toHaveLength(1);
    expect(sentMessages[0]).toMatchObject({
      type: "host:openStatsReport",
      cue: "123456789",
      schoolYear: "2026",
      requestId: expect.any(String)
    });
    expect(container?.textContent).toContain("Informe guardado y abierto en el navegador.");
  });

  it("downloads an HTML blob when the host bridge is unavailable", async () => {
    installFetchMock();
    vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => {});
    const createObjectURL = vi.fn(() => "blob:report");
    const revokeObjectURL = vi.fn();
    vi.stubGlobal("URL", { ...URL, createObjectURL, revokeObjectURL });

    await renderComponent();
    await act(async () => { findButton("Generar informe HTML").click(); await Promise.resolve(); await Promise.resolve(); });

    expect(createObjectURL).toHaveBeenCalledOnce();
    expect(container?.textContent).toContain("Informe HTML descargado.");
  });

  it("shows a loading state and hides previous rows when a stats filter changes", async () => {
    const pendingFilteredRequests: (() => void)[] = [];
    const response = (body: unknown) => ({ ok: true, json: async () => body });
    const fetchMock = vi.fn((input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes("/api/schools?withAttempts=true")) {
        return Promise.resolve(response([{ code: "123456789", name: "Escuela Test", submittedAttemptCount: 2, lastSubmittedAt: "2026-09-30T10:12:00Z" }]));
      }
      if (url.includes("/api/stats/filters?")) {
        return Promise.resolve(response({ schoolYears: ["2025", "2026"], courses: [], exams: [] }));
      }
      if (url.includes("schoolYear=2025")) {
        return new Promise<{ ok: boolean; json: () => Promise<unknown> }>(resolve => {
          pendingFilteredRequests.push(() => resolve(response(
            url.includes("/api/stats/course?")
              ? [{ course: "6", attemptCount: 2, averageScorePercent: 50 }]
              : [{ examVersionId: "exam-v1", examCode: "BIO", versionNumber: 1, attemptCount: 2, averageScorePercent: 50, blocks: [] }]
          )));
        });
      }
      return Promise.resolve(response(
        url.includes("/api/stats/course?")
          ? [{ course: "6", attemptCount: 1, averageScorePercent: 100 }]
          : [{ examVersionId: "exam-v1", examCode: "BIO", versionNumber: 1, attemptCount: 1, averageScorePercent: 100, blocks: [] }]
      ));
    });
    vi.stubGlobal("fetch", fetchMock);

    await renderComponent();
    expect(container?.textContent).toContain("BIO v1 — 1 intentos");
    await act(async () => {
      root?.render(<StatsWorkspace apiBaseUrl="http://localhost" cue="123456789" schoolYear="2025" onBack={() => undefined} />);
      await Promise.resolve();
      await Promise.resolve();
    });

    expect(container?.textContent).toContain("Actualizando estadísticas…");
    expect(container?.textContent).not.toContain("BIO v1 — 1 intentos");
    expect(pendingFilteredRequests).toHaveLength(2);

    await act(async () => {
      pendingFilteredRequests.forEach(resolve => resolve());
      await Promise.resolve();
      await Promise.resolve();
    });
    expect(container?.textContent).toContain("BIO v1 — 2 intentos");
  });

  it("polls every 15 seconds and keeps requests sequential", async () => {
    vi.useFakeTimers();
    const pendingRequests: (() => void)[] = [];
    const fetchMock = vi.fn((input: RequestInfo | URL) => {
      if (String(input).includes("/api/schools?withAttempts=true")) {
        return Promise.resolve({ ok: true, json: async () => [{ code: "123456789", name: "Escuela Test", submittedAttemptCount: 2, lastSubmittedAt: "2026-09-30T10:12:00Z" }] });
      }
      if (String(input).includes("/api/stats/filters?")) {
        return Promise.resolve({ ok: true, json: async () => ({ schoolYears: [], courses: [], exams: [] }) });
      }
      return new Promise<{ ok: boolean; json: () => Promise<unknown> }>(resolve => {
        pendingRequests.push(() => resolve({ ok: true, json: async () => [] }));
      });
    });
    vi.stubGlobal("fetch", fetchMock);
    await renderComponent();
    expect(fetchMock).toHaveBeenCalledTimes(4);

    await act(async () => { await vi.advanceTimersByTimeAsync(15000); });
    expect(fetchMock).toHaveBeenCalledTimes(4);

    await act(async () => {
      pendingRequests.splice(0).forEach(resolve => resolve());
      await Promise.resolve();
      await Promise.resolve();
    });
    await act(async () => { await vi.advanceTimersByTimeAsync(15000); });

    expect(fetchMock).toHaveBeenCalledTimes(6);
    expect(container?.textContent).toContain("Actualizado hace 15s");
  });

  it("pauses polling while hidden and resumes when visible", async () => {
    vi.useFakeTimers();
    const fetchMock = installFetchMock();
    await renderComponent();
    expect(fetchMock).toHaveBeenCalledTimes(4);

    await act(async () => {
      Object.defineProperty(document, "visibilityState", { configurable: true, value: "hidden" });
      document.dispatchEvent(new Event("visibilitychange"));
      await vi.advanceTimersByTimeAsync(30000);
    });
    expect(fetchMock).toHaveBeenCalledTimes(4);

    await act(async () => {
      Object.defineProperty(document, "visibilityState", { configurable: true, value: "visible" });
      document.dispatchEvent(new Event("visibilitychange"));
      await Promise.resolve();
      await Promise.resolve();
    });
    expect(fetchMock).toHaveBeenCalledTimes(6);
  });

  it("refreshes immediately when requested manually", async () => {
    const fetchMock = installFetchMock();
    await renderComponent();
    expect(fetchMock).toHaveBeenCalledTimes(4);

    await act(async () => {
      findButton("Actualizar ahora").click();
      await Promise.resolve();
      await Promise.resolve();
    });

    expect(fetchMock).toHaveBeenCalledTimes(6);
  });

  async function renderComponent(): Promise<void> {
    container = document.createElement("div");
    document.body.append(container);
    root = createRoot(container);
    await act(async () => {
      root?.render(<StatsWorkspace apiBaseUrl="http://localhost" cue="123456789" schoolYear="2026" onBack={() => undefined} />);
      await Promise.resolve();
      await Promise.resolve();
    });
  }

  async function flushEffects(): Promise<void> {
    await act(async () => { for (let index = 0; index < 6; index++) await Promise.resolve(); });
  }

  function findButton(text: string): HTMLButtonElement {
    const button = [...(container?.querySelectorAll("button") ?? [])].find(item => item.textContent === text);
    if (!button) throw new Error(`Button not found: ${text}`);
    return button;
  }
});

function installFetchMock(): ReturnType<typeof vi.fn> {
  const fetchMock = vi.fn(async (input: RequestInfo | URL) => ({
    ok: true,
    json: async () => String(input).includes("/api/schools?withAttempts=true")
      ? [{ code: "123456789", name: "Escuela Test", submittedAttemptCount: 2, lastSubmittedAt: "2026-09-30T10:12:00Z" }]
      : String(input).includes("/api/stats/filters?") ? { schoolYears: [], courses: [], exams: [] } : [],
    blob: async () => new Blob(["<html>snapshot</html>"], { type: "text/html" })
  }));
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

function installStatsFilterFetch(filters: { schoolYears: string[]; courses: string[]; sections: { course: string; division: string; shift?: string | null }[]; exams: [] }): ReturnType<typeof vi.fn> {
  const fetchMock = vi.fn(async (input: RequestInfo | URL) => ({
    ok: true,
    json: async () => String(input).includes("/api/schools?withAttempts=true")
      ? [{ code: "123456789", name: "Escuela Test", submittedAttemptCount: 1, lastSubmittedAt: "2026-09-30T10:12:00Z" }]
      : String(input).includes("/api/stats/filters?") ? filters : [],
    blob: async () => new Blob()
  }));
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}
