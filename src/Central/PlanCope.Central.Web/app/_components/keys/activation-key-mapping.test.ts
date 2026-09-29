import { describe, it, expect } from "vitest";
import { activationKeyStatus, buildReissuePayload, type ActivationKeyStatusSource } from "./activation-key-mapping";

const now = new Date("2026-09-28T12:00:00.000Z");

const baseKey: ActivationKeyStatusSource = {
  revokedAt: null,
  expiresAt: null,
  maxActivations: 5,
  activationCount: 0
};

describe("activationKeyStatus", () => {
  it("marca activa una clave vigente con usos disponibles", () => {
    expect(activationKeyStatus(baseKey, now)).toStrictEqual({ label: "Activa", tone: "active" });
  });

  it("marca revocada cuando tiene revokedAt, sin importar lo demas", () => {
    expect(
      activationKeyStatus({ ...baseKey, revokedAt: "2026-09-01T00:00:00.000Z", activationCount: 9 }, now)
    ).toStrictEqual({ label: "Revocada", tone: "revoked" });
  });

  it("marca vencida cuando expiresAt ya paso", () => {
    expect(
      activationKeyStatus({ ...baseKey, expiresAt: "2026-09-27T00:00:00.000Z" }, now)
    ).toStrictEqual({ label: "Vencida", tone: "expired" });
  });

  it("no marca vencida cuando expiresAt todavia no llego", () => {
    expect(
      activationKeyStatus({ ...baseKey, expiresAt: "2026-09-29T00:00:00.000Z" }, now).label
    ).toBe("Activa");
  });

  it("marca agotada cuando se alcanzaron las activaciones maximas", () => {
    expect(activationKeyStatus({ ...baseKey, activationCount: 5 }, now)).toStrictEqual({
      label: "Agotada",
      tone: "exhausted"
    });
  });

  it("prioriza vencida sobre agotada", () => {
    expect(
      activationKeyStatus(
        { ...baseKey, activationCount: 5, expiresAt: "2026-09-01T00:00:00.000Z" },
        now
      ).label
    ).toBe("Vencida");
  });

  it("ignora una fecha de vencimiento invalida", () => {
    expect(activationKeyStatus({ ...baseKey, expiresAt: "no-es-fecha" }, now).label).toBe("Activa");
  });
});

describe("buildReissuePayload", () => {
  it("normaliza expiresAt y note undefined a null (no a undefined)", () => {
    const payload = buildReissuePayload({ maxActivations: 3, expiresAt: undefined, note: undefined });

    expect(payload).toEqual({ maxActivations: 3, expiresAt: null, note: null });
    expect(payload.expiresAt).not.toBeUndefined();
    expect(payload.note).not.toBeUndefined();
    expect(JSON.parse(JSON.stringify(payload))).toEqual({
      maxActivations: 3,
      expiresAt: null,
      note: null
    });
  });

  it("pasa los valores reales sin alterarlos, incluida una nota con token issued-for-cue", () => {
    const note = "Sede Norte issued-for-cue:12345";
    const payload = buildReissuePayload({
      maxActivations: 10,
      expiresAt: "2027-03-01T00:00:00.000Z",
      note
    });

    expect(payload).toEqual({
      maxActivations: 10,
      expiresAt: "2027-03-01T00:00:00.000Z",
      note
    });
    expect(payload.note).toContain("issued-for-cue:12345");
  });
});
