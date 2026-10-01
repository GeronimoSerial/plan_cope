import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { ExamConfirmationPanel } from "./ExamConfirmationPanel";

describe("ExamConfirmationPanel", () => {
  it("confirms the delivery without displaying a confirmation code", () => {
    const html = renderToStaticMarkup(<ExamConfirmationPanel />);

    expect(html).toContain("student-success-icon");
    expect(html).toContain("Examen enviado");
    expect(html).toContain("Tu entrega se registró. Ya podés cerrar esta ventana o avisarle a tu docente.");
    expect(html).not.toContain("Código de confirmación");
    expect(html).not.toContain("Copiar código");
    expect(html).not.toContain("CONF-9876");
  });

  it("renders the formatted submission time line for a valid ISO timestamp", () => {
    const html = renderToStaticMarkup(
      <ExamConfirmationPanel submittedAt="2026-01-15T12:34:56.000Z" />
    );

    expect(html).toContain("Entregado a las");
  });

  it("omits the submission time line when submittedAt is undefined", () => {
    const html = renderToStaticMarkup(<ExamConfirmationPanel />);

    expect(html).not.toContain("Entregado a las");
  });

  it("does not throw and omits the time line for an unparseable submittedAt", () => {
    const html = renderToStaticMarkup(<ExamConfirmationPanel submittedAt="not-a-date" />);

    expect(html).not.toContain("Entregado a las");
  });
});
