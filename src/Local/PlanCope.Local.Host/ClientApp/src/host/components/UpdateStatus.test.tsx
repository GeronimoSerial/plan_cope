// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { UpdateStatus as UpdateStatusData } from "../types";
import { UpdateStatus } from "./UpdateStatus";

Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
const noop = () => undefined;

describe("UpdateStatus", () => {
  let root: Root | undefined;
  let container: HTMLDivElement | undefined;
  afterEach(() => {
    if (root) act(() => root?.unmount());
    root = undefined;
    container?.remove();
    container = undefined;
    document.body.replaceChildren();
    vi.restoreAllMocks();
  });

  function render(status: UpdateStatusData) {
    container = document.createElement("div");
    container.className = "app-nav-band";
    document.body.append(container);
    root = createRoot(container);
    act(() => root?.render(<UpdateStatus appVersion="1.2.3" status={status} onCheckForUpdates={noop} onDownloadUpdate={noop} onDeferUpdate={noop} onApplyUpdate={noop} />));
    const text = document.body.textContent ?? "";
    expect(document.body.querySelector(`[hidden]`)).toBeNull();
    return text;
  }

  it("shows the installed version and a check action", () => {
    expect(render({ state: "idle" })).toContain("Versión 1.2.3");
    expect(container?.textContent).toContain("Buscar actualizaciones");
  });

  it("shows checking copy and disables the check action", () => {
    render({ state: "checking" });
    expect(container?.textContent).toContain("Buscando actualizaciones…");
    expect(container?.querySelector("button")?.disabled).toBe(true);
  });

  it("renders available updates in a colored body portal with accessible dialog controls", () => {
    render({ state: "updateAvailable", targetVersion: "2.0.0" });
    const dialog = document.body.querySelector<HTMLElement>('[role="dialog"]');
    expect(dialog).not.toBeNull();
    expect(dialog?.parentElement?.parentElement).toBe(document.body);
    expect(dialog?.className).toBe("update-prompt");
    expect(dialog?.getAttribute("aria-modal")).toBe("true");
    expect(dialog?.textContent).toContain("Hay una nueva versión 2.0.0 disponible");
    expect(dialog?.textContent).toContain("Actualizar ahora");
    expect(dialog?.textContent).toContain("Más tarde");
    expect(document.activeElement?.textContent).toBe("Actualizar ahora");
  });

  it("returns focus to the control that opened the dialog", () => {
    render({ state: "idle" });
    const trigger = container?.querySelector<HTMLButtonElement>("button");
    trigger?.focus();
    act(() => root?.render(<UpdateStatus appVersion="1.2.3" status={{ state: "updateAvailable", targetVersion: "2.0.0" }} onCheckForUpdates={noop} onDownloadUpdate={noop} onDeferUpdate={noop} onApplyUpdate={noop} />));
    expect(document.activeElement?.textContent).toBe("Actualizar ahora");

    act(() => root?.render(<UpdateStatus appVersion="1.2.3" status={{ state: "idle" }} onCheckForUpdates={noop} onDownloadUpdate={noop} onDeferUpdate={noop} onApplyUpdate={noop} />));
    expect(document.activeElement).toBe(trigger);
  });

  it("shows visible download progress and percent in a body portal", () => {
    render({ state: "downloading", targetVersion: "2.0.0", progress: 42 });
    const notice = document.body.querySelector<HTMLElement>(".update-notice");
    expect(notice?.textContent).toContain("Descargando actualización 2.0.0");
    expect(notice?.querySelector("progress")?.value).toBe(42);
    expect(notice?.textContent).toContain("42%");
    expect(container?.textContent).toContain("Descarga 42%");
  });

  it("shows an indeterminate download state when percentage data is missing", () => {
    render({ state: "downloading", targetVersion: "2.0.0" });
    const notice = document.body.querySelector<HTMLElement>(".update-notice");
    expect(notice?.querySelector("progress")?.hasAttribute("value")).toBe(false);
    expect(notice?.textContent).toContain("Progreso sin dato");
    expect(notice?.textContent).not.toContain("0%");
    expect(container?.textContent).toContain("Descargando actualización");
  });

  it("shows restart status and manual restart action when offered", () => {
    render({ state: "readyToRestart", restartAvailable: true });
    expect(document.body.textContent).toContain("Reiniciando en la nueva versión…");
    expect(document.body.textContent).toContain("Reiniciar ahora");
  });

  it.each(["readyPendingSessionClose", "updateAvailablePendingSession"] as const)("shows session blockers for %s", state => {
    render({ state, targetVersion: "2.0.0", blockingSessions: [{ id: "s-1", label: "Sesión ABC123 · 180000000 · active" }] });
    const notice = document.body.querySelector(".update-notice");
    expect(notice?.textContent).toContain("sesión activa");
    expect(notice?.textContent).toContain("Sesión ABC123");
    expect(notice?.querySelector('a[href="#sesiones"]')?.textContent).toBe("Ver sesiones");
  });

  it("shows integrity failure and retry action", () => {
    render({ state: "integrityFailed" });
    expect(document.body.textContent).toContain("no superó la verificación de integridad");
    expect(document.body.textContent).toContain("Reintentar");
  });

  it("shows errors and retry action", () => {
    render({ state: "error", message: "Fallo de red" });
    expect(document.body.textContent).toContain("Fallo de red");
    expect(document.body.textContent).toContain("Reintentar");
  });

  it("shows the up-to-date confirmation", () => {
    render({ state: "upToDate" });
    expect(document.body.textContent).toContain("Ya tenés la última versión instalada.");
  });
});
