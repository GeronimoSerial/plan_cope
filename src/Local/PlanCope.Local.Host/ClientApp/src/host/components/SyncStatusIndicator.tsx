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

  return <p role="status">Sincronizado.</p>;
}