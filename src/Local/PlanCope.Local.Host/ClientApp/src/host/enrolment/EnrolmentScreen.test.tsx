import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { EnrolmentScreen, isValidActivationKeyFormat } from "./EnrolmentScreen";

const VALID_KEY = "PCOPE-ABCDE-FGHJK-MNPQR-MZ";
const VALID_KEY_COMPACT = "PCOPEABCDEFGHJKMNPQRMZ";

describe("isValidActivationKeyFormat", () => {
  it("accepts a key with a correct checksum", () => {
    expect(isValidActivationKeyFormat(VALID_KEY)).toBe(true);
    expect(isValidActivationKeyFormat(VALID_KEY_COMPACT)).toBe(true);
  });

  it("rejects a key with a corrupted checksum", () => {
    expect(isValidActivationKeyFormat("PCOPE-ABCDE-FGHJK-MNPQR-MY")).toBe(false);
    expect(isValidActivationKeyFormat("PCOPE-ABCDE-FGHJK-MNPQR-NZ")).toBe(false);
  });

  it("rejects a wrong brand", () => {
    expect(isValidActivationKeyFormat("PCOPA-ABCDE-FGHJK-MNPQR-MZ")).toBe(false);
    expect(isValidActivationKeyFormat("PCOPI-ABCDE-FGHJK-MNPQR-MZ")).toBe(false);
    expect(isValidActivationKeyFormat("PCOPEABCDEFGHJKMNPQRMZ")).toBe(true);
    expect(isValidActivationKeyFormat("PCOPEABCDEFGHJKMNPQRMZ".slice(1))).toBe(false);
  });

  it("rejects a wrong length", () => {
    expect(isValidActivationKeyFormat("")).toBe(false);
    expect(isValidActivationKeyFormat("PCOPE-ABCDE-FGHJK-MNPQR")).toBe(false);
    expect(isValidActivationKeyFormat("PCOPE-ABCDE-FGHJK-MNPQR-M")).toBe(false);
    expect(isValidActivationKeyFormat("PCOPE-ABCDE-FGHJK-MNPQR-MZZ")).toBe(false);
    expect(isValidActivationKeyFormat(`${VALID_KEY_COMPACT}0`)).toBe(false);
  });

  it("rejects characters outside the Crockford base32 alphabet", () => {
    expect(isValidActivationKeyFormat("PCOPE-ABCDE-FGHJI-MNPQR-MZ")).toBe(false);
    expect(isValidActivationKeyFormat("PCOPE-ABCDE-FGHJL-MNPQR-MZ")).toBe(false);
    expect(isValidActivationKeyFormat("PCOPE-ABCDE-FGHJO-MNPQR-MZ")).toBe(false);
    expect(isValidActivationKeyFormat("PCOPE-ABCDE-FGHJU-MNPQR-MZ")).toBe(false);
    expect(isValidActivationKeyFormat("PCOPE-ABCDE-FGHJK-MNPQR-M1")).toBe(false);
  });

  it("normalizes dashes, spacing and case before validating", () => {
    expect(isValidActivationKeyFormat("pcope-abcde-fghjk-mnpqr-mz")).toBe(true);
    expect(isValidActivationKeyFormat("pcope abcde fghjk mnpqr mz")).toBe(true);
    expect(isValidActivationKeyFormat("  PCOPE  ABCDE\tFGHJK\r\nMNPQR-MZ  ")).toBe(true);
  });
});

describe("EnrolmentScreen", () => {
  it("renders the format hint and a disabled submit button on the initial empty render", () => {
    const html = renderToStaticMarkup(
      <EnrolmentScreen apiBaseUrl="http://127.0.0.1:5055" />
    );
    expect(html).toContain("Formato: PCOPE-XXXXX-XXXXX-XXXXX-CC");
    expect(html).toContain("disabled");
    expect(html).toContain("Inscribir equipo");
    expect(html).not.toContain("Equipo inscripto correctamente.");
  });
});