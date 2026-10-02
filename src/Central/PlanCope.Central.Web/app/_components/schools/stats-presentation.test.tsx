import { createElement } from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { Freshness, StatsMetricValue, WeightedFormulaNote } from "./stats-presentation";

describe("school statistics presentation", () => {
  it("keeps a measured zero distinct from suppressed and unavailable metrics", () => {
    const html = renderToStaticMarkup(createElement("div", null,
      createElement(StatsMetricValue, { metric: { value: 0, status: "available" } }),
      createElement(StatsMetricValue, { metric: { value: null, status: "suppressed" } }),
      createElement(StatsMetricValue, { metric: { value: null, status: "unavailable" } })
    ));

    expect(html).toContain(">0<");
    expect(html).toContain("Suprimido");
    expect(html).toContain("No disponible");
  });

  it("labels missing freshness separately and explains the weighted formula", () => {
    const html = renderToStaticMarkup(createElement("div", null,
      createElement(Freshness, { value: null }),
      createElement(WeightedFormulaNote)
    ));

    expect(html).toContain("Sin resultados incorporados");
    expect(html).toContain("Porcentaje ponderado de puntaje");
    expect(html).toContain("no alumnos ni sesiones");
  });
});
