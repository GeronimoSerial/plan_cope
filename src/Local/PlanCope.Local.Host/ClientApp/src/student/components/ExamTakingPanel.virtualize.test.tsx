import { renderToStaticMarkup } from "react-dom/server";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { LocalExamBlock } from "../../shared/api-types";
import type { AnswerMap } from "../domain/examAnswers";
import { ExamTakingPanel } from "./ExamTakingPanel";
import { computeWindowBounds, QuestionNav, scrollBlockIntoView } from "./QuestionNav";

function makeShortAnswerBlock(index: number): LocalExamBlock {
  return {
    id: `block-${index}`,
    localExamVersionId: "exam-v1",
    remoteBlockId: `remote-${index}`,
    orderIndex: index,
    blockType: 4,
    configJson: JSON.stringify({ prompt: `Pregunta ${index}` }),
    validationJson: JSON.stringify({ required: false })
  };
}

function makeBlocks(count: number): LocalExamBlock[] {
  return Array.from({ length: count }, (_, index) => makeShortAnswerBlock(index));
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

function installFakeWindow(scrollY: number, innerHeight = 800) {
  (globalThis as Record<string, unknown>).window = {
    scrollY,
    innerHeight,
    addEventListener: () => undefined,
    removeEventListener: () => undefined
  };
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

describe("ExamTakingPanel virtualization", () => {
  afterEach(() => {
    restoreGlobals();
    vi.restoreAllMocks();
  });

  it("keeps every block wrapper mounted in DOM order with a reserved height and its id", () => {
    const blocks = makeBlocks(150);
    const html = renderPanel(blocks);

    for (let index = 0; index < blocks.length; index++) {
      expect(html).toContain(`id="block-${index}"`);
    }

    const order = blocks.map(block => html.indexOf(`id="${block.id}"`));
    const sorted = [...order].sort((a, b) => a - b);
    expect(order).toEqual(sorted);
  });

  it("renders only near-viewport blocks fully and placeholders for far-off blocks", () => {
    const blocks = makeBlocks(150);
    const html = renderPanel(blocks);

    expect(slotFor(html, "block-2")).toContain('data-state="rendered"');
    expect(html).toContain('id="answer-block-2"');

    expect(slotFor(html, "block-140")).toContain('data-state="placeholder"');
    expect(html).not.toContain('id="answer-block-140"');
    expect(html).toContain("student-question-placeholder");

    const renderedCount = (html.match(/<textarea/g) ?? []).length;
    expect(renderedCount).toBeGreaterThan(0);
    expect(renderedCount).toBeLessThan(150);
  });

  it("renders far-off blocks as placeholders even when the exam is short", () => {
    const blocks = makeBlocks(150);
    const html = renderToStaticMarkup(
      <QuestionNav blocks={blocks} answers={emptyProps.answers} />
    );

    expect(html).toContain('data-nav-block="block-2"');
    expect(html).toMatch(/data-nav-block="block-2"[^>]*data-state="rendered"/);

    expect(html).toMatch(/data-nav-block="block-140"[^>]*data-state="placeholder"/);
    expect(html).not.toContain("Pregunta 141:");
  });

  it("scrolls an off-screen question into view through its always-mounted wrapper id", () => {
    const element = { scrollIntoView: vi.fn() };
    (globalThis as Record<string, unknown>).document = {
      getElementById: (id: string) => (id === "block-140" ? element : null)
    };

    scrollBlockIntoView("block-140");
    expect(element.scrollIntoView).toHaveBeenCalledWith({ behavior: "smooth", block: "start" });

    const html = renderPanel(makeBlocks(150));
    expect(html).toContain('id="block-140"');
    expect(slotFor(html, "block-140")).toContain('data-state="placeholder"');
  });

  it("swaps placeholder for real content when the scroll window moves onto the block", () => {
    installFakeWindow(0, 800);
    expect(renderPanel(makeBlocks(150))).not.toContain('id="answer-block-140"');

    installFakeWindow(140 * 172, 800);
    const html = renderPanel(makeBlocks(150));
    expect(html).toContain('id="answer-block-140"');
    expect(slotFor(html, "block-140")).toContain('data-state="rendered"');
  });

  it("preserves answers from the answers map across scroll-away-and-back", () => {
    const blocks = makeBlocks(150);
    const answers: AnswerMap = { "block-2": "mi respuesta" };

    installFakeWindow(0, 800);
    const atTop = renderPanel(blocks, answers);
    expect(atTop).toContain('id="answer-block-2"');
    expect(atTop).toContain("mi respuesta");

    installFakeWindow(140 * 172, 800);
    const farAway = renderPanel(blocks, answers);
    expect(farAway).toContain('id="answer-block-140"');
    expect(farAway).not.toContain('id="answer-block-2"');
    expect(slotFor(farAway, "block-2")).toContain('data-state="placeholder"');

    installFakeWindow(0, 800);
    const backAtTop = renderPanel(blocks, answers);
    expect(backAtTop).toContain('id="answer-block-2"');
    expect(backAtTop).toContain("mi respuesta");
  });

  it("computes a window of roughly 2 viewport-heights beyond the visible area", () => {
    const bounds = computeWindowBounds({
      scrollTop: 0,
      viewportHeight: 800,
      itemCount: 150,
      itemStep: 172
    });
    expect(bounds.start).toBe(0);
    expect(bounds.end).toBe(14);

    const centered = computeWindowBounds({
      scrollTop: 50 * 172,
      viewportHeight: 800,
      itemCount: 150,
      itemStep: 172
    });
    expect(centered.start).toBe(40);
    expect(centered.end).toBe(64);
  });
});