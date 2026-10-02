// @vitest-environment jsdom

import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { StatsPanel } from "./stats-panel";

const { callCentral } = vi.hoisted(() => ({ callCentral: vi.fn() }));
vi.mock("../../_lib/api/client", () => ({ callCentral }));
vi.mock("./stats-bar-chart", () => ({ default: () => <div>Gráfico de prueba</div> }));

afterEach(() => { cleanup(); callCentral.mockReset(); });

function aggregate(rows = [
  { key: "grade-1", label: "1° año", attemptCount: { value: 0, status: "available" }, weightedScorePercent: { value: 0, status: "available" } },
  { key: "grade-2", label: "2° año", attemptCount: { value: 8, status: "suppressed" }, weightedScorePercent: { value: null, status: "suppressed" } },
  { key: "grade-3", label: "3° año", attemptCount: { value: 9, status: "available" }, weightedScorePercent: { value: null, status: "unavailable" } },
]) {
  return { dataState: "available", generatedAt: "2026-10-01T12:00:00Z", latestRollupUpdatedAt: "2026-10-01T11:00:00Z", dimension: "course", filters: {}, rows, page: 1, pageSize: 50, totalCount: 60 };
}

function setup() {
  callCentral.mockImplementation((path: string) => {
    if (path.startsWith("stats/catalogs?")) {
      const params = new URLSearchParams(path.split("?")[1]);
      return Promise.resolve({ page: Number(params.get("page")), pageSize: 50, totalCount: 75, items: [{ value: `${params.get("dimension")}-1`, label: `Opción ${params.get("dimension")}` }] });
    }
    return Promise.resolve(aggregate());
  });
}

describe("StatsPanel", () => {
  it("shows a loading status while the aggregate request is pending", () => {
    callCentral.mockImplementation((path: string) => path.startsWith("stats/catalogs?")
      ? Promise.resolve({ page: 1, pageSize: 50, totalCount: 0, items: [] })
      : new Promise(() => {}));
    render(<StatsPanel />);
    expect(screen.getByText("Cargando agregados…").getAttribute("role")).toBe("status");
  });

  it("keeps real zero, suppression, and unavailable metrics distinct in an accessible table", async () => {
    setup();
    render(<StatsPanel />);

    expect(await screen.findByText("0")).toBeTruthy();
    expect(screen.getAllByText("Cohorte insuficiente").length).toBeGreaterThan(0);
    expect(screen.getByText("No disponible")).toBeTruthy();
    expect(screen.getByRole("table")).toBeTruthy();
    expect(screen.getAllByRole("columnheader")).toHaveLength(3);
    expect(screen.getByText(/La tabla contiene los mismos valores disponibles que el gráfico/)).toBeTruthy();
    expect(screen.getByText(/100 × ΣScore \/ ΣScoreMax/)).toBeTruthy();
    expect(screen.getByText(/no representa personas únicas/)).toBeTruthy();
  });

  it("paginates searchable catalogs without loading the chart until requested", async () => {
    setup();
    render(<StatsPanel />);
    await screen.findByText("0");
    await screen.findByRole("option", { name: "Opción subject" });
    expect(screen.queryByText("Gráfico de prueba")).toBeNull();

    fireEvent.click(screen.getByRole("button", { name: "Página siguiente de Materia" }));
    await waitFor(() => expect(callCentral).toHaveBeenCalledWith(expect.stringContaining("dimension=subject&page=2")));
    fireEvent.change(screen.getByLabelText("Buscar materia"), { target: { value: "lengua" } });
    await waitFor(() => expect(callCentral).toHaveBeenCalledWith(expect.stringContaining("query=lengua")));

    fireEvent.click(screen.getByRole("button", { name: "Ver gráfico de barras" }));
    expect(await screen.findByText("Gráfico de prueba")).toBeTruthy();
    expect(screen.getByRole("table")).toBeTruthy();
  });

  it("combines selected filters with one allowlisted grouping and uses capped result pages", async () => {
    setup();
    render(<StatsPanel />);
    await screen.findByText("0");
    await screen.findByRole("option", { name: "Opción department" });
    await screen.findByRole("option", { name: "Opción subject" });
    await screen.findByRole("option", { name: "Opción version" });

    fireEvent.change(screen.getByLabelText("Seleccionar departamento"), { target: { value: "department-1" } });
    fireEvent.change(screen.getByLabelText("Seleccionar materia"), { target: { value: "subject-1" } });
    fireEvent.change(screen.getByLabelText("Seleccionar examen / versión"), { target: { value: "version-1" } });
    fireEvent.change(screen.getByLabelText("Agrupar resultados por"), { target: { value: "version" } });
    fireEvent.click(screen.getByText("Aplicar filtros"));

    await waitFor(() => expect(callCentral).toHaveBeenCalledWith(expect.stringContaining("groupBy=version")));
    const combinedQuery = callCentral.mock.calls.map(([path]) => String(path)).filter(path => path.includes("groupBy=version")).at(-1);
    expect(combinedQuery).toContain("departmentId=department-1");
    expect(combinedQuery).toContain("subject=subject-1");
    expect(combinedQuery).toContain("version=version-1");
    expect(combinedQuery).toContain("pageSize=50");

    fireEvent.click(screen.getByRole("button", { name: "Siguiente" }));
    await waitFor(() => expect(callCentral).toHaveBeenCalledWith(expect.stringContaining("groupBy=version&page=2")));
    const pages = callCentral.mock.calls.map(([path]) => String(path)).filter(path => path.startsWith("stats/aggregate?"));
    expect(pages.every(path => Number(new URLSearchParams(path.split("?")[1]).get("pageSize")) <= 100)).toBe(true);
  });

  it("hides opaque version IDs in the searchable version catalog", async () => {
    callCentral.mockImplementation((path: string) => {
      if (!path.startsWith("stats/catalogs?")) return Promise.resolve(aggregate([]));
      const params = new URLSearchParams(path.split("?")[1]);
      return Promise.resolve({ page: 1, pageSize: 50, totalCount: 1, items: params.get("dimension") === "version"
        ? [{ value: "opaque-version-id", label: "opaque-version-id" }]
        : [] });
    });
    render(<StatsPanel />);
    expect(await screen.findByRole("option", { name: "Versión 1 · título no disponible" })).toBeTruthy();
    expect(screen.queryByRole("option", { name: "opaque-version-id" })).toBeNull();
  });

  it("renders an empty result state separately from an error", async () => {
    callCentral.mockImplementation((path: string) => path.startsWith("stats/catalogs?")
      ? Promise.resolve({ page: 1, pageSize: 50, totalCount: 0, items: [] })
      : Promise.resolve({ ...aggregate([]), dataState: "no_results", totalCount: 0 }));
    render(<StatsPanel />);
    expect(await screen.findByText("No hay resultados para los filtros aplicados.")).toBeTruthy();
  });

  it("never exposes an exam identifier when the version contract lacks a title", async () => {
    callCentral.mockImplementation((path: string) => path.startsWith("stats/catalogs?")
      ? Promise.resolve({ page: 1, pageSize: 50, totalCount: 0, items: [] })
      : Promise.resolve({ ...aggregate([{ key: "14000000-0000-4000-8000-000000000001", label: "14000000-0000-4000-8000-000000000001", attemptCount: { value: 10, status: "available" }, weightedScorePercent: { value: 75, status: "available" } }]), dimension: "version" }));
    render(<StatsPanel />);
    expect(await screen.findByText("Versión agrupada 1 · título no disponible")).toBeTruthy();
    expect(screen.queryByText("14000000-0000-4000-8000-000000000001")).toBeNull();
  });

  it("uses the human title and version label once the aggregate contract provides them", async () => {
    callCentral.mockImplementation((path: string) => path.startsWith("stats/catalogs?")
      ? Promise.resolve({ page: 1, pageSize: 50, totalCount: 0, items: [] })
      : Promise.resolve({ ...aggregate([{ key: "opaque-version-id", label: "Matemática · versión 2", attemptCount: { value: 10, status: "available" }, weightedScorePercent: { value: 75, status: "available" } }]), dimension: "version" }));
    render(<StatsPanel />);
    expect(await screen.findByText("Matemática · versión 2")).toBeTruthy();
    expect(screen.queryByText("opaque-version-id")).toBeNull();
  });

  it("shows API failures as errors instead of converting them to empty data", async () => {
    callCentral.mockImplementation((path: string) => path.startsWith("stats/catalogs?")
      ? Promise.resolve({ page: 1, pageSize: 50, totalCount: 0, items: [] })
      : Promise.reject(new Error("falló la conexión")));
    render(<StatsPanel />);
    expect(await screen.findByText("falló la conexión")).toBeTruthy();
    expect(screen.queryByText("No hay resultados para los filtros aplicados.")).toBeNull();
  });
});
