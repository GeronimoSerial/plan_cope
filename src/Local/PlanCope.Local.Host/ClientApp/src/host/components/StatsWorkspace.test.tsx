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

  it("polls every 15 seconds and keeps requests sequential", async () => {
    vi.useFakeTimers();
    const pendingRequests: (() => void)[] = [];
    const fetchMock = vi.fn((input: RequestInfo | URL) => {
      if (String(input).includes("/api/stats/filters?")) {
        return Promise.resolve({ ok: true, json: async () => ({ schoolYears: [], courses: [], exams: [] }) });
      }
      return new Promise<{ ok: boolean; json: () => Promise<unknown> }>(resolve => {
        pendingRequests.push(() => resolve({ ok: true, json: async () => [] }));
      });
    });
    vi.stubGlobal("fetch", fetchMock);
    await renderComponent();
    expect(fetchMock).toHaveBeenCalledTimes(3);

    await act(async () => { await vi.advanceTimersByTimeAsync(15000); });
    expect(fetchMock).toHaveBeenCalledTimes(3);

    await act(async () => {
      pendingRequests.splice(0).forEach(resolve => resolve());
      await Promise.resolve();
      await Promise.resolve();
    });
    await act(async () => { await vi.advanceTimersByTimeAsync(15000); });

    expect(fetchMock).toHaveBeenCalledTimes(5);
    expect(container?.textContent).toContain("Actualizado hace 15s");
  });

  it("pauses polling while hidden and resumes when visible", async () => {
    vi.useFakeTimers();
    const fetchMock = installFetchMock();
    await renderComponent();
    expect(fetchMock).toHaveBeenCalledTimes(3);

    await act(async () => {
      Object.defineProperty(document, "visibilityState", { configurable: true, value: "hidden" });
      document.dispatchEvent(new Event("visibilitychange"));
      await vi.advanceTimersByTimeAsync(30000);
    });
    expect(fetchMock).toHaveBeenCalledTimes(3);

    await act(async () => {
      Object.defineProperty(document, "visibilityState", { configurable: true, value: "visible" });
      document.dispatchEvent(new Event("visibilitychange"));
      await Promise.resolve();
      await Promise.resolve();
    });
    expect(fetchMock).toHaveBeenCalledTimes(5);
  });

  it("refreshes immediately when requested manually", async () => {
    const fetchMock = installFetchMock();
    await renderComponent();
    expect(fetchMock).toHaveBeenCalledTimes(3);

    await act(async () => {
      findButton("Actualizar ahora").click();
      await Promise.resolve();
      await Promise.resolve();
    });

    expect(fetchMock).toHaveBeenCalledTimes(5);
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

  function findButton(text: string): HTMLButtonElement {
    const button = [...(container?.querySelectorAll("button") ?? [])].find(item => item.textContent === text);
    if (!button) throw new Error(`Button not found: ${text}`);
    return button;
  }
});

function installFetchMock(): ReturnType<typeof vi.fn> {
  const fetchMock = vi.fn(async (input: RequestInfo | URL) => ({
    ok: true,
    json: async () => String(input).includes("/api/stats/filters?") ? { schoolYears: [], courses: [], exams: [] } : [],
    blob: async () => new Blob(["<html>snapshot</html>"], { type: "text/html" })
  }));
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}
