import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import type { UpdateStatus as UpdateStatusData } from "../types";
import { UpdateStatus } from "./UpdateStatus";

const noop = () => undefined;

function render(status: UpdateStatusData, appVersion = "1.2.3") {
  return renderToStaticMarkup(
    <UpdateStatus appVersion={appVersion} status={status} onCheckForUpdates={noop} onConfirmRestart={noop} />
  );
}

describe("UpdateStatus", () => {
  it("renders the installed version string passed in", () => {
    expect(render({ state: "idle" })).toContain("Versión 1.2.3");
  });

  it("falls back to desconocida when no version is available", () => {
    const html = renderToStaticMarkup(
      <UpdateStatus status={{ state: "idle" }} onCheckForUpdates={noop} onConfirmRestart={noop} />
    );
    expect(html).toContain("Versión desconocida");
  });

  it("renders the check for updates button", () => {
    expect(render({ state: "idle" })).toContain("Buscar actualizaciones");
  });

  it("shows the checking message and disables the button while checking", () => {
    const html = render({ state: "checking" });
    expect(html).toContain("Buscando actualizaciones…");
    expect(html).toContain('disabled="');
  });

  it("renders nothing extra beyond the version line when not configured", () => {
    const html = render({ state: "notConfigured" });
    expect(html).not.toContain("Buscar actualizaciones");
  });

  it("renders the up-to-date message", () => {
    expect(render({ state: "upToDate" })).toContain("Ya tenés la última versión instalada.");
  });

  it("renders the downloading message with the target version", () => {
    expect(render({ state: "downloading", targetVersion: "2.0.0" })).toContain("2.0.0");
  });

  it("renders the integrity failure message with a retry button", () => {
    const html = render({ state: "integrityFailed" });
    expect(html).toContain("verificación de integridad");
    expect(html).toContain("Buscar actualizaciones");
  });

  it("renders the pending-session-close message", () => {
    expect(render({ state: "readyPendingSessionClose" })).toContain("al finalizar la sesión activa");
  });

  it("renders the restart button when ready to apply", () => {
    expect(render({ state: "readyToApply" })).toContain("Reiniciar y actualizar");
  });

  it("renders the error message when present", () => {
    expect(render({ state: "error", message: "Fallo de red" })).toContain("Fallo de red");
  });

  it("renders a generic error message when none is provided", () => {
    expect(render({ state: "error" })).toContain("No se pudo verificar actualizaciones.");
  });
});