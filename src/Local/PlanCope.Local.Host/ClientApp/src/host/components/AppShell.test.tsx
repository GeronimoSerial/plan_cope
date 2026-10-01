import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it, vi } from "vitest";
import { AppShell } from "./AppShell";
import { StudentShell } from "../../student/components/StudentShell";

vi.mock("../hooks/useUpdateStatus", () => ({
  useUpdateStatus: () => ({
    status: { state: "idle" },
    checkForUpdates: vi.fn(),
    downloadUpdate: vi.fn(),
    deferUpdate: vi.fn(),
    applyUpdate: vi.fn()
  })
}));

vi.mock("../hooks/useSyncStatus", () => ({ useSyncStatus: () => null }));

describe("AppShell", () => {
  it("renders the institutional logo and active section navigation", () => {
    const html = renderToStaticMarkup(
      <AppShell status="Listo" activeTab="history" onTabChange={() => undefined}>
        <p>Contenido</p>
      </AppShell>
    );

    expect(html).toContain('src="/static/logo-educacion-h.svg"');
    expect(html).toContain('<h1 class="app-brand">Plan COPE</h1>');
    expect(html).toContain('aria-label="Secciones principales"');
    expect(html).toContain('aria-current="page">Historial</button>');
    expect(html).toContain('aria-live="polite">Listo</span>');
  });

  it("renders the student product name as the page heading", () => {
    const html = renderToStaticMarkup(<StudentShell><p>Contenido</p></StudentShell>);

    expect(html).toContain("<h1>Plan COPE</h1>");
  });
});
