import { createElement } from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { OperationsSummary } from "./operations-summary";

describe("OperationsSummary", () => {
  it("keeps the published exam count visible when stats scope is denied", () => {
    const html = renderToStaticMarkup(createElement(OperationsSummary, {
      state: { unavailable: "denied" },
      publishedExams: 3
    }));

    expect(html).toContain("Exámenes publicados");
    expect(html).toContain(">3<");
    expect(html).toContain("Las estadísticas no están habilitadas para este alcance.");
    expect(html).toContain("Dato no disponible para este alcance.");
  });

  it("keeps the published count available after a stats service error", () => {
    const html = renderToStaticMarkup(createElement(OperationsSummary, {
      state: { unavailable: "error" },
      publishedExams: 2
    }));

    expect(html).toContain("Exámenes publicados");
    expect(html).toContain(">2<");
    expect(html).toContain("No se pudieron cargar las estadísticas.");
  });
});
