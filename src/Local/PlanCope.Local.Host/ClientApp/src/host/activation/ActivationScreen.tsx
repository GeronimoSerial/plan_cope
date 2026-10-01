import { FormEvent, useEffect, useState } from "react";
import type { NativeBridge } from "../types";

type ActivationScreenProps = { apiBaseUrl: string; bridge?: NativeBridge; activationInProgress?: boolean };
type ErrorResponse = { error?: string; detail?: string };
type DownloadProgress = { phase: string; completed: number; total: number; skipped: number };

export function activationErrorMessage(body: ErrorResponse, fallback: string): string {
  return body.error ?? body.detail ?? fallback;
}

export function activationProgressMessage(progress: DownloadProgress | null): string | null {
  if (!progress) return null;
  if (progress.phase === "exams") return "Descargando evaluaciones…";
  if (progress.phase === "schools") return "Guardando escuelas…";
  if (progress.phase === "rosters") {
    const skipped = progress.skipped > 0 ? ` · ${progress.skipped} omitidas` : "";
    return `Descargando listas: ${progress.completed} de ${progress.total}${skipped}.`;
  }
  if (progress.phase === "complete") return "Listas descargadas. Finalizando activación…";
  return null;
}

export function normalizeActivationKey(value: string): string {
  return value.toUpperCase().replace(/[^A-Z0-9]/g, "");
}

const BASE32 = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
export function isValidActivationKeyFormat(value: string): boolean {
  const normalized = normalizeActivationKey(value);
  if (!/^PCOPE[0-9A-HJKMNPQRSTVWXYZ]{17}$/.test(normalized)) return false;
  let crc = 0xffff;
  for (const char of normalized.slice(5, 20)) {
    crc ^= char.charCodeAt(0) << 8;
    for (let bit = 0; bit < 8; bit++) crc = crc & 0x8000 ? ((crc << 1) ^ 0x1021) & 0xffff : (crc << 1) & 0xffff;
  }
  const checksum = BASE32[(crc & 0x3ff) >> 5] + BASE32[crc & 0x1f];
  return normalized.slice(20) === checksum;
}

export function shouldShowActivation(isActivated: boolean, activationInProgress = false): boolean {
  return !isActivated || activationInProgress;
}

export function canRetryActivationDownload(status: unknown): boolean {
  return typeof status === "object" && status !== null && "activationInProgress" in status &&
    (status as { activationInProgress?: unknown }).activationInProgress === true;
}

export function ActivationScreen({
  apiBaseUrl,
  bridge = typeof window !== "undefined" ? window.chrome?.webview : undefined,
  activationInProgress = false
}: ActivationScreenProps) {
  const [activationKey, setActivationKey] = useState("");
  const [submitted, setSubmitted] = useState(false);
  const [retryAvailable, setRetryAvailable] = useState(activationInProgress);
  const [error, setError] = useState<string | null>(null);
  const [downloadProgress, setDownloadProgress] = useState<DownloadProgress | null>(null);

  useEffect(() => {
    let cancelled = false;
    const refreshStatus = async () => {
      try {
        const response = await fetch(`${apiBaseUrl}/api/activation/status`);
        const data = response.ok ? await response.json() as {
          activationInProgress?: unknown;
          downloadProgress?: DownloadProgress | null;
        } : null;
        if (!cancelled && data) {
          if (canRetryActivationDownload(data)) setRetryAvailable(true);
          setDownloadProgress(data.downloadProgress ?? null);
        }
      } catch { /* The key form remains available if status cannot be read. */ }
    };
    void refreshStatus();
    const interval = submitted ? window.setInterval(() => void refreshStatus(), 1000) : undefined;
    return () => {
      cancelled = true;
      if (interval !== undefined) window.clearInterval(interval);
    };
  }, [apiBaseUrl, submitted]);

  const activate = async (event: FormEvent) => {
    event.preventDefault();
    if (!isValidActivationKeyFormat(activationKey) || submitted) return;
    setSubmitted(true);
    setError(null);
    try {
      const response = await fetch(`${apiBaseUrl}/api/enrolment/redeem`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ activationKey: normalizeActivationKey(activationKey) })
      });
      if (!response.ok) {
        const body = await response.json().catch(() => ({})) as ErrorResponse;
        setError(activationErrorMessage(body, "No se pudo validar la clave. Verificá la conexión e intentá nuevamente."));
        setSubmitted(false);
        return;
      }
      if (bridge) bridge.postMessage({ type: "host:activationComplete" });
      else window.location.reload();
    } catch {
      setError("No se pudo conectar con Central. Verificá la conexión a internet e intentá nuevamente.");
      setSubmitted(false);
    }
  };

  const retryDownload = async () => {
    if (submitted) return;
    setSubmitted(true);
    setError(null);
    try {
      const response = await fetch(`${apiBaseUrl}/api/enrolment/retry-download`, { method: "POST" });
      if (!response.ok) {
        const body = await response.json().catch(() => ({})) as ErrorResponse;
        setError(activationErrorMessage(body, "No se pudieron descargar los datos. Verificá la conexión e intentá nuevamente."));
        setSubmitted(false);
        return;
      }
      if (bridge) bridge.postMessage({ type: "host:activationComplete" });
      else window.location.reload();
    } catch {
      setError("No se pudo conectar con Central. Verificá la conexión a internet e intentá nuevamente.");
      setSubmitted(false);
    }
  };

  return <main className="school-gate">
    <form className="gate-card activation-card" onSubmit={activate}>
      <p className="eyebrow">Activación del equipo</p>
      <h1>Activar Plan Cope Local</h1>
      {retryAvailable ? <>
        <p>La clave ya fue validada. Reintentá la descarga para terminar la activación de este equipo.</p>
        <button type="button" disabled={submitted} onClick={retryDownload}>
          {submitted ? "Descargando datos…" : "Reintentar descarga"}
        </button>
      </> : <>
        <p>Ingresá la clave de activación para registrar este equipo y descargar los datos necesarios.</p>
        <label htmlFor="activation-key">Clave de activación</label>
        <input id="activation-key" name="activationKey" type="text" autoComplete="off" autoCapitalize="characters"
          value={activationKey} disabled={submitted} onChange={event => setActivationKey(event.target.value)} />
        <button type="submit" disabled={!isValidActivationKeyFormat(activationKey) || submitted || !bridge}>
          {submitted ? "Descargando datos…" : "Activar equipo"}
        </button>
      </>}
      {submitted && <p role="status" aria-live="polite">{activationProgressMessage(downloadProgress) ?? "Validando la clave y descargando escuelas, listas y evaluaciones. No cierres la aplicación."}</p>}
      {error && <p role="alert">{error}</p>}
      {!bridge && !retryAvailable && <p role="alert">La activación sólo está disponible dentro de la aplicación de escritorio.</p>}
    </form>
  </main>;
}
