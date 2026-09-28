import { describe, it, expect } from "vitest";
import { buildReissuePayload } from "./activation-key-mapping";

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
