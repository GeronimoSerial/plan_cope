import { FormEvent, useState } from "react";
import { ActionButton, Field, TextInput } from "../../shared/ui";

// Crockford base32 alphabet (excludes I, L, O, U).
const BASE32_ALPHABET = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

const BRAND = "PCOPE";
const BRAND_LENGTH = 5;
const PAYLOAD_LENGTH = 15;
const CHECKSUM_LENGTH = 2;
const KEY_LENGTH = BRAND_LENGTH + PAYLOAD_LENGTH + CHECKSUM_LENGTH;

type EnrolmentScreenProps = {
  apiBaseUrl: string;
  onDone?: () => void; // called after a successful redeem, so the parent can close/hide this screen
};

type RedeemResponse = {
  nodeId?: string;
  error?: string;
};

// Strips cosmetic separators and uppercases, mirroring the server's TryNormalize.
function normalizeActivationKey(rawKey: string): string {
  let normalized = "";
  for (let i = 0; i < rawKey.length; i++) {
    const ch = rawKey[i];
    if (ch === "-" || ch === " " || ch === "\t" || ch === "\r" || ch === "\n") {
      continue;
    }
    normalized += ch.toUpperCase();
  }
  return normalized;
}

// CRC-16/CCITT (poly 0x1021, init 0xFFFF, MSB-first) over the payload's UTF-16 code units.
function computeCrc16(payload: string): number {
  let crc = 0xffff;
  for (let i = 0; i < payload.length; i++) {
    crc ^= (payload.charCodeAt(i) << 8) & 0xffff;
    for (let bit = 0; bit < 8; bit++) {
      crc = crc & 0x8000 ? ((crc << 1) ^ 0x1021) & 0xffff : (crc << 1) & 0xffff;
    }
  }
  return crc;
}

function computeChecksum(payload: string): string {
  const low = computeCrc16(payload) & 0x3ff;
  return BASE32_ALPHABET[(low >> 5) & 0x1f] + BASE32_ALPHABET[low & 0x1f];
}

export function isValidActivationKeyFormat(rawKey: string): boolean {
  const normalized = normalizeActivationKey(rawKey);

  if (normalized.length !== KEY_LENGTH) {
    return false;
  }

  if (normalized.slice(0, BRAND_LENGTH) !== BRAND) {
    return false;
  }

  for (let i = BRAND_LENGTH; i < KEY_LENGTH; i++) {
    if (!BASE32_ALPHABET.includes(normalized[i])) {
      return false;
    }
  }

  const payload = normalized.slice(BRAND_LENGTH, BRAND_LENGTH + PAYLOAD_LENGTH);
  const checksum = computeChecksum(payload);
  return normalized.slice(BRAND_LENGTH + PAYLOAD_LENGTH, KEY_LENGTH) === checksum;
}

export function EnrolmentScreen({ apiBaseUrl, onDone }: EnrolmentScreenProps) {
  const [activationKey, setActivationKey] = useState("");
  const [submitted, setSubmitted] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [succeeded, setSucceeded] = useState(false);

  const isValid = isValidActivationKeyFormat(activationKey);

  const redeem = async (event?: FormEvent) => {
    event?.preventDefault();

    if (!isValid || submitted) {
      return;
    }

    setSubmitError(null);
    setSubmitted(true);

    try {
      const response = await fetch(`${apiBaseUrl}/api/enrolment/redeem`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ activationKey: normalizeActivationKey(activationKey) })
      });

      if (!response.ok) {
        let message = "No se pudo completar la inscripción del equipo. Intentá nuevamente.";
        try {
          const body = (await response.json()) as RedeemResponse;
          if (typeof body.error === "string" && body.error.trim().length > 0) {
            message = body.error;
          }
        } catch {
          // Keep the generic message when the body is not valid JSON.
        }
        setSubmitError(message);
        setSubmitted(false);
        return;
      }

      setSucceeded(true);
      setSubmitted(false);
      onDone?.();
    } catch {
      setSubmitError("No se pudo completar la inscripción del equipo. Intentá nuevamente.");
      setSubmitted(false);
    }
  };

  if (succeeded) {
    return (
      <div className="gate-card">
        <p className="eyebrow">Inscripción del equipo</p>
        <h1>Equipo inscripto correctamente.</h1>
        <p>Este equipo ya puede usar el servicio de exámenes.</p>
      </div>
    );
  }

  return (
    <form className="gate-card" onSubmit={redeem}>
      <p className="eyebrow">Inscripción del equipo</p>
      <h1>Inscribir este equipo</h1>
      <p>
        Ingresá la clave de inscripción entregada con la activación. Podés hacerlo en
        cualquier momento: el equipo sigue funcionando igual sin inscribirse.
      </p>

      <Field
        label="Clave de activación"
        error={
          activationKey.length > 0 && !isValid
            ? "La clave no es válida. Revisá el formato y los caracteres."
            : undefined
        }
      >
        <TextInput
          value={activationKey}
          placeholder="PCOPE-XXXXX-XXXXX-XXXXX-CC"
          autoComplete="off"
          onChange={setActivationKey}
        />
      </Field>
      <p className="field-hint">Formato: PCOPE-XXXXX-XXXXX-XXXXX-CC</p>

      {submitError && (
        <p className="error-banner" role="alert">
          {submitError}
        </p>
      )}

      <ActionButton disabled={!isValid || submitted} onClick={() => void redeem()}>
        {submitted ? "Inscribiendo…" : "Inscribir equipo"}
      </ActionButton>
    </form>
  );
}