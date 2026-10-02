import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import type { SyncStatusDto } from "../api/apiClient";
import { SyncStatusIndicator } from "./SyncStatusIndicator";

function render(status: SyncStatusDto | null): string {
  return renderToStaticMarkup(<SyncStatusIndicator status={status} />);
}

const offlineStatus: SyncStatusDto = {
  healthy: false,
  offline: true,
  lastError: null,
  lastPullAt: "2026-09-16T10:00:00Z",
  lastPushAt: null,
  nextAttempt: null
};

const errorStatus: SyncStatusDto = {
  healthy: false,
  offline: false,
  lastError: "No se pudo contactar a Central",
  lastPullAt: "2026-09-16T10:00:00Z",
  lastPushAt: null,
  nextAttempt: "2026-09-16T10:30:00Z"
};

const healthyStatus: SyncStatusDto = {
  healthy: true,
  offline: false,
  lastError: null,
  lastPullAt: "2026-09-16T10:00:00Z",
  lastPushAt: "2026-09-16T10:00:00Z",
  nextAttempt: null
};

describe("SyncStatusIndicator", () => {
  it("renders nothing while no status is known", () => {
    expect(render(null)).toBe("");
  });

  it("renders calm neutral copy while offline and never the word error", () => {
    const html = render(offlineStatus);
    expect(html).toContain("Sin conexión");
    expect(html).not.toMatch(/error/i);
    expect(html).not.toContain('role="alert"');
    expect(html).not.toMatch(/alert|danger|critical/i);
  });

  it("surfaces the last error text when connected but failing", () => {
    const html = render(errorStatus);
    expect(html).toContain("No se pudo sincronizar");
    expect(html).toContain("No se pudo contactar a Central");
    expect(html).toContain("Se reintentará automáticamente.");
  });

  it("shows the last observed Central activity instead of claiming continuous connection", () => {
    expect(render(healthyStatus)).toContain("Conexión observada");
    expect(render(healthyStatus)).not.toContain("Sincronizado.");
  });

  it("prefers the received heartbeat and durable result acknowledgement timestamps", () => {
    expect(render({ ...healthyStatus, lastHeartbeatReceivedAt: "2026-09-16T10:15:00Z" })).toContain("Heartbeat recibido");
    expect(render({ ...healthyStatus, lastPullAt: null, lastPushAt: null, lastPushAckAt: "2026-09-16T10:15:00Z" })).toContain("Resultados recibidos");
  });

  it("shows a heartbeat error code and HTTP status", () => {
    const html = render({ ...healthyStatus, heartbeatErrorCode: "timeout", heartbeatLastHttpStatus: 504 });
    expect(html).toContain("timeout");
    expect(html).toContain("HTTP 504");
  });

  it("keeps the connection unknown when no communication timestamp is available", () => {
    const noSignal = { ...healthyStatus, lastPullAt: null, lastPushAt: null };
    expect(render(noSignal)).toContain("Sin dato de conexión con Central.");
  });

  it("keeps the offline and error states textually distinct", () => {
    const offline = render(offlineStatus);
    const error = render(errorStatus);
    expect(offline).not.toBe(error);
    expect(offline).not.toContain("No se pudo sincronizar");
    expect(error).not.toContain("Sin conexión");
  });
});
