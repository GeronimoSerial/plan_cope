import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { ExamConfirmationPanel } from "./ExamConfirmationPanel";

describe("ExamConfirmationPanel", () => {
  it("renders the confirmation code inside the code box", () => {
    const html = renderToStaticMarkup(<ExamConfirmationPanel code="CONF-9876" />);

    expect(html).toContain("confirmation-code");
    expect(html).toContain("CONF-9876");
    expect(html).toContain("Copiar código");
  });

  it("renders the formatted submission time line for a valid ISO timestamp", () => {
    const html = renderToStaticMarkup(
      <ExamConfirmationPanel code="CONF-9876" submittedAt="2026-01-15T12:34:56.000Z" />
    );

    expect(html).toContain("Entregado a las");
  });

  it("omits the submission time line when submittedAt is undefined", () => {
    const html = renderToStaticMarkup(<ExamConfirmationPanel code="CONF-9876" />);

    expect(html).not.toContain("Entregado a las");
  });

  it("does not throw and omits the time line for an unparseable submittedAt", () => {
    const html = renderToStaticMarkup(<ExamConfirmationPanel code="CONF-9876" submittedAt="not-a-date" />);

    expect(html).not.toContain("Entregado a las");
  });

  it("always renders the teacher instruction notice", () => {
    const html = renderToStaticMarkup(<ExamConfirmationPanel code="CONF-9876" />);

    expect(html).toContain("Esperá la indicación del docente.");
  });
});
