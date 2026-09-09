import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { ActivationScreen, isValidPassphrase, shouldShowActivation } from "./ActivationScreen";

describe("activation", () => {
  it("requires a non-empty passphrase", () => {
    expect(isValidPassphrase("   ")).toBe(false);
    expect(isValidPassphrase("frase-secreta")).toBe(true);
  });

  it("renders the one-time activation form while no key is stored", () => {
    const html = renderToStaticMarkup(<ActivationScreen bridge={{ postMessage: () => undefined }} />);
    expect(html).toContain("Activar Plan Cope Local");
    expect(html).toContain('type="password"');
  });

  it("does not request activation after a protected key is stored", () => {
    expect(shouldShowActivation(false)).toBe(true);
    expect(shouldShowActivation(true)).toBe(false);
  });
});
