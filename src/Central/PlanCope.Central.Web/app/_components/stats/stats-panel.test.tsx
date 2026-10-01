import { describe, expect, it } from "vitest";
import { createElement } from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { StatsPanel } from "./stats-panel";

describe("StatsPanel", () => {
  it("renders the school list with names and a link to the school directory", () => {
    const html = renderToStaticMarkup(createElement(StatsPanel, {
      initialSchools: [{ cue: "180000100", schoolName: "Escuela Central", attemptCount: 10, averageScorePercent: 72.5 }],
      schoolYears: [{ value: "2026", label: "2026" }],
      scopeDenied: false
    }));

    expect(html).toContain("180000100");
    expect(html).toContain("Escuela Central");
    expect(html).toContain("Intentos");
    expect(html).toContain("/escuelas?q=180000100");
    expect(html).toContain("Todos los años");
  });

  it("renders the suppression label when attemptCount and averageScorePercent are 'cohorte insuficiente'", () => {
    const html = renderToStaticMarkup(createElement(StatsPanel, {
      initialSchools: [{ cue: "180000101", schoolName: "Escuela Chica", attemptCount: "cohorte insuficiente", averageScorePercent: "cohorte insuficiente" }],
      schoolYears: [{ value: "2026", label: "2026" }],
      scopeDenied: false
    }));

    expect(html).toContain("Escuela Chica");
    expect(html.match(/cohorte insuficiente/g)?.length).toBeGreaterThanOrEqual(2);
  });
});
