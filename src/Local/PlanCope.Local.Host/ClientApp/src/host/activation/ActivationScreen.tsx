import { FormEvent, useState } from "react";
import type { NativeBridge } from "../types";

type ActivationScreenProps = { apiBaseUrl: string; bridge?: NativeBridge };
type ErrorResponse = { error?: string };

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

export function shouldShowActivation(isActivated: boolean): boolean {
  return !isActivated;
}

export function ActivationScreen({ apiBaseUrl, bridge = window.chrome?.webview }: ActivationScreenProps) {
  const [activationKey, setActivationKey] = useState("");
  const [submitted, setSubmitted] = useState(false);
  const [error, setError] = useState<string | null>(null);

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
        setError(body.error || "No se pudo validar la clave. Verificá la conexión e intentá nuevamente.");
        setSubmitted(false);
        return;
      }
      bridge?.postMessage({ type: "host:activationComplete" });
    } catch {
      setError("No se pudo conectar con Central. Verificá la conexión a internet e intentá nuevamente.");
      setSubmitted(false);
    }
  };

  return <main className="school-gate">
    <form className="gate-card activation-card" onSubmit={activate}>
      <p className="eyebrow">Activación del equipo</p>
      <h1>Activar Plan Cope Local</h1>
      <p>Ingresá la clave de activación para registrar este equipo y descargar los datos necesarios.</p>
      <label htmlFor="activation-key">Clave de activación</label>
      <input id="activation-key" name="activationKey" type="text" autoComplete="off" autoCapitalize="characters"
        value={activationKey} disabled={submitted} onChange={event => setActivationKey(event.target.value)} />
      <button type="submit" disabled={!isValidActivationKeyFormat(activationKey) || submitted || !bridge}>
        {submitted ? "Activando…" : "Activar equipo"}
      </button>
      {error && <p role="alert">{error}</p>}
      {!bridge && <p role="alert">La activación sólo está disponible dentro de la aplicación de escritorio.</p>}
    </form>
  </main>;
}
