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
    const html = renderToStaticMarkup(<StatsWorkspace apiBaseUrl="http://localhost" cue="123456789" schoolYear="2026" />);

    expect(html).toContain("Año lectivo");
    expect(html).toContain('id="stats-school-year-filter"');
    expect(html).toContain('id="stats-course-filter"');
    expect(html).toContain("Generar informe HTML");
    expect(html).toContain("Pantalla en vivo");
    expect(html).toContain("Descargar CSV");
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

  it("filters course rows and exam sections with the search field", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      const body = url.includes("/api/schools?withAttempts=true")
        ? [{ code: "123456789", name: "Escuela Test", submittedAttemptCount: 2, lastSubmittedAt: "2026-09-30T10:12:00Z" }]
        : url.includes("/api/stats/filters?")
          ? { schoolYears: ["2026"], courses: ["6", "7"], exams: [] }
          : url.includes("/api/stats/course?")
            ? [{ course: "6", attemptCount: 1, averageScorePercent: 80 }, { course: "7", attemptCount: 1, averageScorePercent: 60 }]
            : [{ examVersionId: "math-v1", examCode: "MAT-6", title: "Matemática diagnóstico", courses: ["6"], versionNumber: 1, attemptCount: 1, averageScorePercent: 80, blocks: [] }, { examVersionId: "bio-v1", examCode: "BIO-7", title: "Biología", courses: ["7"], versionNumber: 1, attemptCount: 1, averageScorePercent: 60, blocks: [] }];
      return { ok: true, json: async () => body };
    });
    vi.stubGlobal("fetch", fetchMock);

    await renderComponent();
    await act(async () => {
      container?.querySelector<HTMLInputElement>("#stats-school-filter")?.click();
      await Promise.resolve();
    });
    expect(container?.querySelectorAll('[role="option"]')).toHaveLength(1);
    expect(container?.textContent).toContain("2 entregas · última 30/09/2026");
    const search = container?.querySelector<HTMLInputElement>("#stats-search");
    expect(search).not.toBeNull();
    await act(async () => {
      if (search) {
        const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")?.set;
        setter?.call(search, "matematica");
        search.dispatchEvent(new Event("input", { bubbles: true }));
      }
      await Promise.resolve();
    });

    expect(container?.textContent).toContain("Matemática diagnóstico");
    expect(container?.textContent).not.toContain("Biología");
    expect(container?.textContent).toContain("1 intentos");

    await act(async () => {
      if (search) {
        const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")?.set;
        setter?.call(search, "7");
        search.dispatchEvent(new Event("input", { bubbles: true }));
      }
      await Promise.resolve();
    });
    expect(container?.textContent).toContain("BIO-7");
    expect(container?.textContent).not.toContain("MAT-6");

    await act(async () => {
      if (search) {
        const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")?.set;
        setter?.call(search, "6b");
        search.dispatchEvent(new Event("input", { bubbles: true }));
      }
      await Promise.resolve();
    });
    expect(container?.textContent).toContain("MAT-6");
    expect(container?.textContent).not.toContain("BIO-7");
  });

  it("matches a division suffix only when section data contains that division", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      const body = url.includes("/api/schools?withAttempts=true")
        ? [{ code: "123456789", name: "Escuela Test", submittedAttemptCount: 1, lastSubmittedAt: "2026-09-30T10:12:00Z" }]
        : url.includes("/api/stats/filters?")
          ? { schoolYears: ["2026"], courses: ["6"], exams: [] }
          : url.includes("/api/stats/course?")
            ? [{ course: "6", sections: ["B"], attemptCount: 1, averageScorePercent: 80 }]
            : [{ examVersionId: "math-v1", examCode: "MAT-6", courses: ["6"], sections: [{ course: "6", division: "B" }], versionNumber: 1, attemptCount: 1, averageScorePercent: 80, blocks: [] }];
      return { ok: true, json: async () => body };
    });
    vi.stubGlobal("fetch", fetchMock);

    await renderComponent();
    const search = container?.querySelector<HTMLInputElement>("#stats-search");
    const setSearch = async (value: string) => act(async () => {
      if (search) {
        Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")?.set?.call(search, value);
        search.dispatchEvent(new Event("input", { bubbles: true }));
      }
      await Promise.resolve();
    });
    await setSearch("6b");
    expect(container?.textContent).toContain("MAT-6");
    await setSearch("6a");
    expect(container?.textContent).not.toContain("MAT-6");
    expect(container?.textContent).toContain("Sin resultados para la búsqueda.");
  });

  it.each(["1ro", "3er", "7mo"])("searches course %s using its Spanish grade alias", async alias => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      const body = url.includes("/api/schools?withAttempts=true")
        ? [{ code: "123456789", name: "Escuela Test", submittedAttemptCount: 3, lastSubmittedAt: "2026-09-30T10:12:00Z" }]
        : url.includes("/api/stats/filters?")
          ? { schoolYears: ["2026"], courses: ["1", "3", "7"], exams: [] }
          : url.includes("/api/stats/course?")
            ? ["1", "3", "7"].map(course => ({ course, attemptCount: 1, averageScorePercent: 80 }))
            : ["1", "3", "7"].map(course => ({ examVersionId: `exam-${course}`, examCode: `MAT-${course}`, courses: [course], versionNumber: 1, attemptCount: 1, averageScorePercent: 80, blocks: [] }));
      return { ok: true, json: async () => body };
    });
    vi.stubGlobal("fetch", fetchMock);

    await renderComponent();
    const search = container?.querySelector<HTMLInputElement>("#stats-search");
    await act(async () => {
      if (search) {
        Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")?.set?.call(search, alias);
        search.dispatchEvent(new Event("input", { bubbles: true }));
      }
      await Promise.resolve();
    });

    const expectedGrade = alias.startsWith("1") ? "1" : alias.startsWith("3") ? "3" : "7";
    expect(container?.textContent).toContain(`MAT-${expectedGrade}`);
    for (const otherGrade of ["1", "3", "7"].filter(grade => grade !== expectedGrade)) {
      expect(container?.textContent).not.toContain(`MAT-${otherGrade}`);
    }
  });

  it("shows the school empty state and disables exports when there are no submitted attempts", async () => {
    const fetchMock = vi.fn(async () => ({ ok: true, json: async () => [] }));
    vi.stubGlobal("fetch", fetchMock);

    await renderComponent();

    expect(container?.textContent).toContain("Todavía no hay exámenes entregados en este equipo.");
    expect(findButton("Generar informe HTML").disabled).toBe(true);
    expect(findButton("Descargar CSV").disabled).toBe(true);
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
      root?.render(<StatsWorkspace apiBaseUrl="http://localhost" cue="123456789" schoolYear="2025" />);
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
      root?.render(<StatsWorkspace apiBaseUrl="http://localhost" cue="123456789" schoolYear="2026" />);
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
