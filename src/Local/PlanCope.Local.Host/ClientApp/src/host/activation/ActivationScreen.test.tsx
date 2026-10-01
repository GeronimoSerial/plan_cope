import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { ActivationScreen, activationErrorMessage, activationProgressMessage, canRetryActivationDownload, isValidActivationKeyFormat, shouldShowActivation } from "./ActivationScreen";

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

  it("opens the download retry action immediately when activation is pending", () => {
    const html = renderToStaticMarkup(
      <ActivationScreen apiBaseUrl="http://127.0.0.1:5055" activationInProgress />
    );

    expect(html).toContain("La clave ya fue validada. Reintentá la descarga");
    expect(html).toMatch(/<button type="button">Reintentar descarga<\/button>/);
  });

  it("shows activation only when the node is not enrolled", () => {
    expect(shouldShowActivation(false)).toBe(true);
    expect(shouldShowActivation(true)).toBe(false);
    expect(shouldShowActivation(true, true)).toBe(true);
  });

  it("offers download retry only for a redeem that already stored credentials", () => {
    expect(canRetryActivationDownload({ activationInProgress: true })).toBe(true);
    expect(canRetryActivationDownload({ activationInProgress: false })).toBe(false);
    expect(canRetryActivationDownload(null)).toBe(false);
  });

  it("shows the server detail message returned by a 502 download failure", () => {
    expect(activationErrorMessage({ detail: "Central devolvió un error específico." }, "fallback"))
      .toBe("Central devolvió un error específico.");
  });

  it("shows completed and skipped roster counts during activation download", () => {
    expect(activationProgressMessage({ phase: "rosters", completed: 127, total: 2042, skipped: 3 }))
      .toBe("Descargando listas: 127 de 2042 · 3 omitidas.");
  });
});
