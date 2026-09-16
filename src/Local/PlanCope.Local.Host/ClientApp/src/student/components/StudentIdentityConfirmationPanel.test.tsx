import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { StudentIdentityConfirmationPanel } from "./StudentIdentityConfirmationPanel";
import type { ResolvedStudent } from "../types";

const student: ResolvedStudent = {
  displayName: "Ana María Gómez",
  maskedDocument: "****678",
  firstName: "Ana María",
  lastName: "Gómez"
};

const baseProps = {
  student,
  isBusy: false,
  error: "",
  onConfirm: () => undefined,
  onCorrect: () => undefined
};

describe("StudentIdentityConfirmationPanel", () => {
  it("renders the student display name and masked document", () => {
    const html = renderToStaticMarkup(<StudentIdentityConfirmationPanel {...baseProps} />);

    expect(html).toContain("Ana María Gómez");
    expect(html).toContain("****678");
  });

  it("renders both buttons enabled when not busy", () => {
    const html = renderToStaticMarkup(<StudentIdentityConfirmationPanel {...baseProps} isBusy={false} />);

    expect(html).toContain("Sí, soy yo");
    expect(html).toContain("Volver y corregir");
    expect(html).not.toContain("disabled");
  });

  it("renders both buttons disabled and swaps the confirm label when busy", () => {
    const html = renderToStaticMarkup(<StudentIdentityConfirmationPanel {...baseProps} isBusy={true} />);

    expect(html).toContain("disabled");
    expect(html).toContain("Iniciando…");
    expect(html).not.toContain("Sí, soy yo");
    expect(html).toContain("Volver y corregir");
  });

  it("renders a non-empty error inside the alert banner", () => {
    const html = renderToStaticMarkup(
      <StudentIdentityConfirmationPanel {...baseProps} error="No pudimos iniciar el examen." />
    );

    expect(html).toContain('role="alert"');
    expect(html).toContain("error-banner");
    expect(html).toContain("No pudimos iniciar el examen.");
  });

  it("renders no alert element when the error is empty", () => {
    const html = renderToStaticMarkup(<StudentIdentityConfirmationPanel {...baseProps} error="" />);

    expect(html).not.toContain('role="alert"');
    expect(html).not.toContain("error-banner");
  });
});
