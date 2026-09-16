import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { SessionEntryPanel } from "./SessionEntryPanel";

const baseProps = {
  sessionCode: "ABC-123",
  document: "12345678",
  isBusy: false,
  onSessionCodeChange: () => undefined,
  onDocumentChange: () => undefined,
  onResolveStudent: () => undefined
};

describe("SessionEntryPanel", () => {
  it("renders the not-found prompt as neutral copy without the error banner", () => {
    const html = renderToStaticMarkup(
      <SessionEntryPanel
        {...baseProps}
        error=""
        notFoundPrompt={{
          message: "No encontramos ese DNI en el padrón.",
          hint: "Revisá el número e intentá de nuevo."
        }}
      />
    );

    expect(html).toContain("No encontramos ese DNI en el padrón.");
    expect(html).toContain("Revisá el número e intentá de nuevo.");
    expect(html).not.toContain("error-banner");
    expect(html).not.toContain('role="alert"');
    expect(html).toContain("Buscar mis datos");
  });

  it("still renders the generic error banner for other errors", () => {
    const html = renderToStaticMarkup(
      <SessionEntryPanel {...baseProps} error="Falló la conexión con la API." notFoundPrompt={null} />
    );

    expect(html).toContain("Falló la conexión con la API.");
    expect(html).toContain("error-banner");
    expect(html).toContain('role="alert"');
  });
});
