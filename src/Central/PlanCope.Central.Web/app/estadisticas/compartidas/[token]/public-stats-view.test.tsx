// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import { PublicStatsView } from "./public-stats-view";

vi.mock("../../../_components/stats/stats-bar-chart", () => ({ default: () => <div>Gráfico público</div> }));
const snapshot = {
  groupBy: "course", groupLabel: "Curso", filters: { schoolYear: "2026" }, generatedAt: "2026-10-01T10:00:00Z", expiresAt: "2026-10-08T10:00:00Z",
  totalAttempts: { value: null, status: "suppressed" }, totalWeightedScorePercent: { value: null, status: "suppressed" },
  rows: [
    { label: "Datos suprimidos por privacidad", attemptCount: { value: null, status: "suppressed" }, weightedScorePercent: { value: null, status: "suppressed" } },
    { label: "6° grado", attemptCount: { value: 8, status: "available" }, weightedScorePercent: { value: 72.5, status: "available" } },
  ],
};

afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

describe("PublicStatsView", () => {
  it("renders only snapshot aggregates, including suppression and weighted formula", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => snapshot }));
    render(<PublicStatsView token={"A".repeat(43)} />);
    expect((await screen.findAllByText("Suprimido por privacidad")).length).toBe(4);
    expect(screen.getByText("72.5%")).toBeTruthy();
    expect(screen.getByRole("table")).toBeTruthy();
    expect(screen.getByText(/100 × ΣScore \/ ΣScoreMax/)).toBeTruthy();
    expect(screen.queryByText("4° grado")).toBeNull();
    expect(screen.queryByText(/180000100|version-private|student|answer|session/i)).toBeNull();
  });

  it("shows the same unavailable state for an absent, expired, or revoked token", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue({ ok: false, status: 404 }));
    render(<PublicStatsView token={"B".repeat(43)} />);
    expect(await screen.findByText("Este enlace no está disponible.")).toBeTruthy();
  });
});
