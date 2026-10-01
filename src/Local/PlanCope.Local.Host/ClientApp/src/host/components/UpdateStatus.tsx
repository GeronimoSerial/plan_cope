import type { UpdateStatus as UpdateStatusData } from "../types";

type UpdateStatusProps = {
  appVersion?: string;
  status: UpdateStatusData;
  onCheckForUpdates: () => void;
  onDownloadUpdate: () => void;
  onDeferUpdate: () => void;
};

export function UpdateStatus({ appVersion, status, onCheckForUpdates, onDownloadUpdate, onDeferUpdate }: UpdateStatusProps) {
  const { state, targetVersion, message } = status;
  const isChecking = state === "checking";

  const checkForUpdatesButton = (
    <button type="button" onClick={onCheckForUpdates} disabled={isChecking}>
      Buscar actualizaciones
    </button>
  );

  return (
    <div className="update-status">
      <p>Versión {appVersion ?? "desconocida"}</p>

      {(state === "idle" || state === "upToDate" || state === "error") && (
        <>
          {checkForUpdatesButton}
          {state === "upToDate" && <p role="status">Ya tenés la última versión instalada.</p>}
          {state === "error" && (
            <p role="status">{message ?? "No se pudo verificar actualizaciones."}</p>
          )}
        </>
      )}

      {state === "checking" && (
        <>
          <p role="status">Buscando actualizaciones…</p>
          {checkForUpdatesButton}
        </>
      )}

      {state === "downloading" && (
        <p role="status">Descargando actualización{targetVersion ? ` ${targetVersion}` : ""}</p>
      )}

      {state === "updateAvailable" && (
        <div className="update-prompt-backdrop" role="presentation">
          <section className="update-prompt" role="dialog" aria-modal="true" aria-labelledby="update-prompt-title">
            <h2 id="update-prompt-title">Hay una nueva versión {targetVersion} disponible. ¿Querés actualizar?</h2>
            <div className="update-prompt-actions">
              <button type="button" onClick={onDownloadUpdate}>Actualizar</button>
              <button type="button" onClick={onDeferUpdate}>Más tarde</button>
            </div>
          </section>
        </div>
      )}

      {state === "integrityFailed" && (
        <>
          <p role="status">
            La descarga no superó la verificación de integridad. Se reintentará en la próxima verificación.
          </p>
          {checkForUpdatesButton}
        </>
      )}

      {state === "readyPendingSessionClose" && (
        <p role="status">Actualización lista. Se aplicará al finalizar la sesión activa.</p>
      )}

      {state === "readyToRestart" && (
        <p role="status">Actualización lista. Reiniciando…</p>
      )}
    </div>
  );
}
