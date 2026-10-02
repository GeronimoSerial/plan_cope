import { renderToStaticMarkup } from "react-dom/server";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { LocalExamBlock } from "../../shared/api-types";
import type { AnswerMap } from "../domain/examAnswers";
import { ExamTakingPanel } from "./ExamTakingPanel";
import { QuestionNav, scrollBlockIntoView } from "./QuestionNav";

function makeMultipleChoiceBlock(index: number): LocalExamBlock {
  return {
    id: `block-${index}`,
    localExamVersionId: "exam-v1",
    remoteBlockId: `remote-${index}`,
    orderIndex: index,
    blockType: 2,
    configJson: JSON.stringify({ question: `Pregunta ${index}`, options: [{ value: "a", label: "A" }] }),
    validationJson: JSON.stringify({ required: false })
  };
}

function makeBlocks(count: number): LocalExamBlock[] {
  return Array.from({ length: count }, (_, index) => makeMultipleChoiceBlock(index));
}

const emptyProps = {
  blocks: makeBlocks(0),
  answers: {} as AnswerMap,
  missingRequired: new Set<string>(),
  isBusy: false,
  status: "",
  error: "",
  onAnswerChange: () => undefined,
  onSave: () => undefined,
  onSubmit: () => undefined
};

function renderPanel(blocks: LocalExamBlock[], answers: AnswerMap = {}) {
  return renderToStaticMarkup(
    <ExamTakingPanel
      blocks={blocks}
      answers={answers}
      missingRequired={new Set()}
      isBusy={false}
      status=""
      error=""
      onAnswerChange={() => undefined}
      onSave={() => undefined}
      onSubmit={() => undefined}
    />
  );
}

function restoreGlobals() {
  delete (globalThis as Record<string, unknown>).window;
  delete (globalThis as Record<string, unknown>).document;
}

function slotFor(html: string, blockId: string): string {
  const match = new RegExp(`<div[^>]*data-block-id="${blockId}"[^>]*>`).exec(html);
  if (!match) {
    throw new Error(`no wrapper slot found for ${blockId}`);
  }
  return match[0];
}

describe("ExamTakingPanel question navigation", () => {
  afterEach(() => {
    restoreGlobals();
    vi.restoreAllMocks();
  });

  it("renders all demo questions and their answers in DOM order without height placeholders", () => {
    const blocks = makeBlocks(8);
    const html = renderPanel(blocks);

    for (let index = 0; index < blocks.length; index++) {
      expect(html).toContain(`id="block-${index}"`);
    }

    const order = blocks.map(block => html.indexOf(`id="${block.id}"`));
    const sorted = [...order].sort((a, b) => a - b);
    expect(order).toEqual(sorted);
    expect((html.match(/data-block-id="block-\d+" data-state="rendered"/g) ?? []).length).toBe(8);
    expect(html).not.toContain("student-question-placeholder");
    expect(html).not.toContain('data-state="placeholder"');
  });

  it("shows blocking paused and closed notices and disables exam actions", () => {
    const paused = renderToStaticMarkup(<ExamTakingPanel {...emptyProps} sessionStatus="paused" />);
    expect(paused).toContain("La sesión está pausada por el docente. Tus respuestas están guardadas.");
    expect(paused).toContain('class="student-session-notice"');
    expect(paused).toContain("Guardar respuestas</button>");
    expect(paused).toMatch(/Guardar respuestas<\/button>/);
    expect(paused).toMatch(/<button[^>]*disabled=""[^>]*>Guardar respuestas/);

    const closed = renderToStaticMarkup(<ExamTakingPanel {...emptyProps} sessionStatus="closed" />);
    expect(closed).toContain("El docente cerró la sesión. Tu examen fue entregado.");
    expect(closed).toContain('class="student-session-notice"');
    expect(closed).toMatch(/<button[^>]*disabled=""[^>]*>Enviar examen/);
  });

  it("locks answers during submission and marks pending saves as a warning", () => {
    const html = renderToStaticMarkup(
      <ExamTakingPanel {...emptyProps} isBusy={true} status="Pendiente por conexión." />
    );
    expect(html).toMatch(/<fieldset class="student-question-lock" disabled=""/);
    expect(html).toContain("messagebar-warning");
    expect(html).toContain("Pendiente por conexión.");
  });

  it("keeps far-off question content mounted so navigation never targets a placeholder", () => {
    const blocks = makeBlocks(150);
    const html = renderPanel(blocks);

    expect(slotFor(html, "block-2")).toContain('data-state="rendered"');
    expect(html).toContain("Pregunta 2");
    expect(slotFor(html, "block-140")).toContain('data-state="rendered"');
    expect(html).toContain("Pregunta 140");
    expect(html).not.toContain("student-question-placeholder");
  });

  it("renders every navigation link as an actionable button", () => {
    const blocks = makeBlocks(150);
    const html = renderToStaticMarkup(
      <QuestionNav blocks={blocks} answers={emptyProps.answers} />
    );

    expect(html).toContain('data-nav-block="block-2"');
    expect(html).toMatch(/data-nav-block="block-2"[^>]*data-state="rendered"/);
    expect(html).toMatch(/data-nav-block="block-140"[^>]*data-state="rendered"/);
    expect(html).toContain('aria-label="Pregunta 141: Pendiente"');
  });

  it("scrolls to an off-screen question whose full content is mounted", () => {
    const element = { scrollIntoView: vi.fn(), querySelector: () => null };
    (globalThis as Record<string, unknown>).document = {
      getElementById: (id: string) => (id === "block-140" ? element : null)
    };

    scrollBlockIntoView("block-140");
    expect(element.scrollIntoView).toHaveBeenCalledWith({ behavior: "smooth", block: "start" });

    const html = renderPanel(makeBlocks(150));
    expect(html).toContain('id="block-140"');
    expect(slotFor(html, "block-140")).toContain('data-state="rendered"');
  });

  it("retains answer values in both the question control and navigation status", () => {
    const html = renderPanel(makeBlocks(8), { "block-2": "a" });
    expect(html).toContain('value="a"');
    expect(html).toContain('aria-label="Pregunta 3: Respondida"');
  });
});
