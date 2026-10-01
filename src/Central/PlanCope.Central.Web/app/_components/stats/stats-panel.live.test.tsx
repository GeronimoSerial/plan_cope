// @vitest-environment jsdom

import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import { StatsPanel } from "./stats-panel";

vi.mock("../../_lib/api/client", () => ({ callCentral: vi.fn().mockResolvedValue([]) }));

afterEach(cleanup);

describe("StatsPanel live session badge", () => {
  it("shows current in-progress and submitted counts beside final school stats", () => {
    render(<StatsPanel initialSchools={[{
      cue: "180055400",
      attemptCount: 20,
      averageScorePercent: 72.5,
      liveSessionCount: 1,
      liveJoinedCount: 8,
      liveInProgressCount: 5,
      liveSubmittedCount: 3
    }]} />);

    expect(screen.getByText("En curso · 8 alumnos · 5 en evaluación · 3 entregados")).toBeTruthy();
    expect(screen.getByText("20")).toBeTruthy();
  });
});
