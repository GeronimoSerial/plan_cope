import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { SessionCreatePanel } from "./SessionCreatePanel";

const noop = () => undefined;

function render(syncPull: { isPulling: boolean; message: string | null; lastPullAt: string | null }) {
  return renderToStaticMarkup(
    <SessionCreatePanel
      exams={[]}
      formErrors={{}}
      selectedExamId=""
      isBusy={false}
      isLoadingExams={false}
      onCreateSession={noop}
      onRefreshExams={noop}
      onSelectedExamChange={noop}
      syncPull={{ ...syncPull, pullExamsNow: noop }}
      roster={{
        snapshot: null,
        sections: [],
        selectedSectionId: "",
        setSelectedSectionId: noop,
        isLoading: false,
        error: null
      }}
    />
  );
}

describe("SessionCreatePanel exam pull", () => {
  it("renders the on-demand pull button when idle", () => {
    const html = render({ isPulling: false, message: null, lastPullAt: null });
    expect(html).toContain("Buscar exámenes nuevos");
    expect(html).not.toContain("Buscando…");
  });

  it("disables the button and shows the spinner label while pulling", () => {
    const html = render({ isPulling: true, message: null, lastPullAt: null });
    expect(html).toContain("Buscando…");
    expect(html).toContain('disabled=""');
  });

  it("renders the server message inline", () => {
    const html = render({ isPulling: false, message: "Se importó 1 examen nuevo.", lastPullAt: null });
    expect(html).toContain("Se importó 1 examen nuevo.");
  });

  it("renders the last successful pull time", () => {
    const html = render({ isPulling: false, message: null, lastPullAt: "2026-09-28T13:45:00Z" });
    expect(html).toContain("Última búsqueda:");
  });
});
