export interface ReissuePayloadSource {
  maxActivations: number;
  expiresAt?: string | null;
  note?: string | null;
}

export function buildReissuePayload(source: ReissuePayloadSource) {
  return {
    maxActivations: source.maxActivations,
    expiresAt: source.expiresAt ?? null,
    note: source.note ?? null
  };
}

export interface ActivationKeyStatusSource {
  revokedAt?: string | null;
  expiresAt?: string | null;
  maxActivations: number;
  activationCount: number;
}

export type ActivationKeyTone = "active" | "revoked" | "expired" | "exhausted";

export interface ActivationKeyStatus {
  label: string;
  tone: ActivationKeyTone;
}

/**
 * Estado visible de una clave. Orden de precedencia: revocada, vencida, agotada, activa.
 * Una fecha de vencimiento inválida no marca la clave como vencida.
 */
export function activationKeyStatus(
  key: ActivationKeyStatusSource,
  now: Date = new Date()
): ActivationKeyStatus {
  if (key.revokedAt) {
    return { label: "Revocada", tone: "revoked" };
  }

  if (key.expiresAt) {
    const expiresAt = new Date(key.expiresAt).getTime();
    if (!Number.isNaN(expiresAt) && expiresAt < now.getTime()) {
      return { label: "Vencida", tone: "expired" };
    }
  }

  if (key.activationCount >= key.maxActivations) {
    return { label: "Agotada", tone: "exhausted" };
  }

  return { label: "Activa", tone: "active" };
}
