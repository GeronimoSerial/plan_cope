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
