import type { SyncStatusDto } from "../api/apiClient";

type SyncStatusIndicatorProps = {
  status: SyncStatusDto | null;
};

function formatTime(value: string | null): string | null {
  if (!value) {
    return null;
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return null;
  }

  return date.toLocaleTimeString("es-AR", { hour: "2-digit", minute: "2-digit" });
}

export function SyncStatusIndicator({ status }: SyncStatusIndicatorProps) {
  if (!status) {
    return null;
  }

  if (status.offline) {
    return <p role="status">Sin conexión. Los datos se sincronizarán al reconectar.</p>;
  }

  if (status.lastError) {
    const nextAttempt = formatTime(status.nextAttempt);
    return (
      <p role="status">
        No se pudo sincronizar: {status.lastError}. Se reintentará automáticamente.
        {nextAttempt ? ` Próximo intento: ${nextAttempt}` : ""}
      </p>
    );
  }

  if (status.heartbeatErrorCode) {
    const nextAttempt = formatTime(status.nextAttempt);
    const httpStatus = status.heartbeatLastHttpStatus ? ` (HTTP ${status.heartbeatLastHttpStatus})` : "";
    return <p role="status">Heartbeat: {status.heartbeatErrorCode}{httpStatus}.{nextAttempt ? ` Próximo intento: ${nextAttempt}` : ""}</p>;
  }

  const receivedHeartbeatAt = formatTime(status.lastHeartbeatReceivedAt ?? null);
  if (receivedHeartbeatAt) return <p role="status">Heartbeat recibido · {receivedHeartbeatAt}.</p>;

  const receivedResultsAt = formatTime(status.lastPushAckAt ?? null);
  if (receivedResultsAt) return <p role="status">Resultados recibidos · {receivedResultsAt}.</p>;

  const latestObservedAt = formatTime(status.lastPushAt) ?? formatTime(status.lastPullAt);
  return latestObservedAt
    ? <p role="status">Conexión observada · {latestObservedAt}.</p>
    : <p role="status">Sin dato de conexión con Central.</p>;
}
