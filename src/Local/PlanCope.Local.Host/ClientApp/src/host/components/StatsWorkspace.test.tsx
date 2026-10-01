import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { StatsWorkspace } from "./StatsWorkspace";

describe("StatsWorkspace", () => {
  it("renders report controls, known filter selectors, and a useful empty state", () => {
    const html = renderToStaticMarkup(<StatsWorkspace apiBaseUrl="http://localhost" cue="123456789" schoolYear="2026" />);

    expect(html).toContain("Año lectivo");
    expect(html).toContain('id="stats-school-year-filter"');
    expect(html).toContain('id="stats-course-filter"');
    expect(html).toContain("Generar informe HTML");
    expect(html).toContain("/api/stats/report.html?cue=123456789&amp;schoolYear=2026");
    expect(html).toContain("Descargar CSV");
    expect(html).toContain("Todavía no hay intentos entregados");
  });
});
