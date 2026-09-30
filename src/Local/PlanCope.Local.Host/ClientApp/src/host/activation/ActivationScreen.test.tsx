import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { ActivationScreen, isValidActivationKeyFormat, shouldShowActivation } from "./ActivationScreen";

describe("activation", () => {
  it("validates the PCOPE checksum and accepts cosmetic separators", () => {
    expect(isValidActivationKeyFormat("PCOPE-ABCDE-FGHJK-MNPQR-M1")).toBe(false);
    expect(isValidActivationKeyFormat("PCOPEABCDEFGHJKMNPQRMZ")).toBe(true);
    expect(isValidActivationKeyFormat("PCOPE-ABCDEFGHJKMNPQRMZ")).toBe(true);
  });

  it("asks only for the activation key", () => {
    const html = renderToStaticMarkup(<ActivationScreen apiBaseUrl="http://127.0.0.1:5055" bridge={{ postMessage: () => undefined }} />);
    expect(html).toContain("Activar Plan Cope Local");
    expect(html).toContain("Clave de activación");
    expect(html).not.toContain("Frase secreta");
    expect(html).not.toContain("CUE");
  });

  it("shows activation only when the node is not enrolled", () => {
    expect(shouldShowActivation(false)).toBe(true);
    expect(shouldShowActivation(true)).toBe(false);
  });
});
