import { createElement } from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { LiveSessionsPanel } from "./live-sessions-panel";
import type { LiveSessionSummary } from "../../_lib/api/server";

const baseSession: LiveSessionSummary = {
  sessionId: "session-1",
  cue: "180055400",
  schoolYear: "2026",
  rosterSectionId: "section-1",
  examVersionId: "exam-version-1",
  status: "active",
  joinedCount: 3,
  inProgressCount: 1,
  submittedCount: 1,
  closedOrForcedCount: 0,
  startedAt: "2026-10-02T12:00:00Z",
  lastActivityAt: null,
  lastHeartbeatAt: "2026-10-02T12:00:00Z",
  signalStatus: "fresh",
  appVersion: "1.0.0",
  heartbeatStaleAfterSeconds: 600
};

describe("LiveSessionsPanel", () => {
  it("shows lifecycle and distinct fresh, stale, and missing heartbeat states", () => {
    const now = Date.now();
    const html = renderToStaticMarkup(createElement(LiveSessionsPanel, {
      initialReadStatus: null,
      initialSessions: [
        { ...baseSession, sessionId: "active", lastHeartbeatAt: new Date(now - 20_000).toISOString(), signalStatus: "fresh" },
        { ...baseSession, sessionId: "paused", status: "paused", lastHeartbeatAt: new Date(now - 700_000).toISOString(), signalStatus: "stale" },
        { ...baseSession, sessionId: "missing", lastHeartbeatAt: null, signalStatus: "missing" },
        { ...baseSession, sessionId: "closed", status: "closed", lastHeartbeatAt: new Date(now - 900_000).toISOString(), signalStatus: "stale" }
      ]
    }));

    expect(html).toContain("Activa");
    expect(html).toContain("Pausada");
    expect(html).toContain("Cerrada");
    expect(html).toContain("Fresca");
    expect(html).toContain("Desactualizada");
    expect(html).toContain("Sin señal");
  });

  it("distinguishes an empty response from a failed read", () => {
    const empty = renderToStaticMarkup(createElement(LiveSessionsPanel, { initialSessions: [], initialReadStatus: null }));
    const failed = renderToStaticMarkup(createElement(LiveSessionsPanel, { initialSessions: [], initialReadStatus: "error" }));

    expect(empty).toContain("No hay sesiones informadas.");
    expect(failed).toContain("No se pudo leer el estado de las sesiones.");
    expect(failed).toContain("No hay una lectura disponible.");
  });

  it("shows a scope denial separately from a read error", () => {
    const denied = renderToStaticMarkup(createElement(LiveSessionsPanel, { initialSessions: [], initialReadStatus: "denied" }));
    expect(denied).toContain("No tenés permiso para ver estas sesiones.");
    expect(denied).not.toContain("No se pudo leer el estado de las sesiones.");
  });
});
