import { useEffect, useRef } from "react";
import { createPortal } from "react-dom";
import type { UpdateStatus as UpdateStatusData } from "../types";

type UpdateStatusProps = {
  appVersion?: string;
  status: UpdateStatusData;
  onCheckForUpdates: () => void;
  onDownloadUpdate: () => void;
  onDeferUpdate: () => void;
  onApplyUpdate?: () => void;
};

export function UpdateStatus({ appVersion, status, onCheckForUpdates, onDownloadUpdate, onDeferUpdate, onApplyUpdate }: UpdateStatusProps) {
  const { state, targetVersion, message, progress, restartAvailable, blockingSessions } = status;
  const isChecking = state === "checking";
  const dialogRef = useRef<HTMLElement>(null);
  const returnFocusRef = useRef<HTMLElement | null>(null);
  const checkForUpdatesButton = (
    <button type="button" onClick={event => { returnFocusRef.current = event.currentTarget; onCheckForUpdates(); }} disabled={isChecking}>
      {isChecking ? "Buscando actualizaciones…" : "Buscar actualizaciones"}
    </button>
  );

  useEffect(() => {
    if (state !== "updateAvailable") return;
    const dialog = dialogRef.current;
    const activeElement = document.activeElement;
    if (!returnFocusRef.current?.isConnected) {
      returnFocusRef.current = activeElement instanceof HTMLElement && activeElement !== document.body ? activeElement : null;
    }
    const focusable = dialog?.querySelector<HTMLElement>("button");
    focusable?.focus();
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") onDeferUpdate();
      if (event.key === "Tab" && dialog) {
        const buttons = Array.from(dialog.querySelectorAll<HTMLElement>("button:not(:disabled)"));
        if (buttons.length < 2) return;
        if (event.shiftKey && document.activeElement === buttons[0]) {
          event.preventDefault(); buttons[buttons.length - 1].focus();
        } else if (!event.shiftKey && document.activeElement === buttons[buttons.length - 1]) {
          event.preventDefault(); buttons[0].focus();
        }
      }
    };
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("keydown", onKeyDown);
      if (returnFocusRef.current?.isConnected) returnFocusRef.current.focus();
      returnFocusRef.current = null;
    };
  }, [state, onDeferUpdate]);

  const dialog = state === "updateAvailable" && createPortal(
    <div className="update-prompt-backdrop" role="presentation">
      <section ref={dialogRef} className="update-prompt" role="dialog" aria-modal="true" aria-labelledby="update-prompt-title" tabIndex={-1}>
        <h2 id="update-prompt-title">Hay una nueva versión {targetVersion ?? "nueva"} disponible</h2>
        <p>Instale la actualización para continuar usando la versión más reciente.</p>
        <div className="update-prompt-actions">
          <button type="button" onClick={onDownloadUpdate}>Actualizar ahora</button>
          <button type="button" onClick={onDeferUpdate}>Más tarde</button>
        </div>
      </section>
    </div>, document.body
  );

  const visibleStates = ["downloading", "readyToRestart", "readyPendingSessionClose", "updateAvailablePendingSession", "integrityFailed", "error", "upToDate"].includes(state);
  const panel = visibleStates && createPortal(
    <section className={`update-notice update-notice-${state}`} role="status" aria-live="polite" aria-label="Estado de actualización">
      {state === "downloading" && <>
        <strong>Descargando actualización{targetVersion ? ` ${targetVersion}` : ""}</strong>
        <progress max={100} value={progress ?? undefined} aria-label="Progreso de descarga" />
        <span>{progress == null ? "Progreso sin dato" : `${progress}%`}</span>
      </>}
      {state === "readyToRestart" && <>
        <strong>Reiniciando en la nueva versión…</strong>
        {message && <p>{message}</p>}
        {restartAvailable && onApplyUpdate && <button type="button" onClick={onApplyUpdate}>Reiniciar ahora</button>}
      </>}
      {(state === "readyPendingSessionClose" || state === "updateAvailablePendingSession") && <>
        <strong>{state === "readyPendingSessionClose" ? "La actualización está lista y se aplicará cuando termine la sesión activa" : `La versión ${targetVersion ?? "nueva"} se aplicará cuando termine la sesión activa`}</strong>
        {!!blockingSessions?.length && <ul>{blockingSessions.map(session => <li key={session.id}>{session.label}</li>)}</ul>}
        {message && <p>{message}</p>}
        <a href="#sesiones">Ver sesiones</a>
      </>}
      {state === "integrityFailed" && <>
        <strong>La descarga no superó la verificación de integridad.</strong>
        {message && <p>{message}</p>}
        <button type="button" onClick={onDownloadUpdate}>Reintentar</button>
      </>}
      {state === "error" && <>
        <strong>{message ?? "No se pudo verificar actualizaciones."}</strong>
        <button type="button" onClick={onCheckForUpdates}>Reintentar</button>
      </>}
      {state === "upToDate" && <strong>Ya tenés la última versión instalada.</strong>}
    </section>, document.body
  );

  return <>
    <div className="update-status" aria-label="Actualizaciones">
      <span className="update-version">Versión {appVersion ?? "desconocida"}</span>
      {checkForUpdatesButton}
      {state === "updateAvailable" && <span className="update-state-label">Actualización disponible</span>}
      {state === "downloading" && <span className="update-state-label">{progress == null ? "Descargando actualización" : `Descarga ${progress}%`}</span>}
    </div>
    {dialog}
    {panel}
  </>;
}
