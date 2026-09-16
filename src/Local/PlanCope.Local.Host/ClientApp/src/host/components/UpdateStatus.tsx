import type { UpdateStatus as UpdateStatusData } from "../types";

type UpdateStatusProps = {
  appVersion?: string;
  status: UpdateStatusData;
  onCheckForUpdates: () => void;
  onConfirmRestart: () => void;
};

export function UpdateStatus({ appVersion, status, onCheckForUpdates, onConfirmRestart }: UpdateStatusProps) {
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

      {state === "readyToApply" && (
        <>
          <p role="status">Actualización lista para instalar.</p>
          <button type="button" onClick={onConfirmRestart}>
            Reiniciar y actualizar
          </button>
        </>
      )}
    </div>
  );
}
