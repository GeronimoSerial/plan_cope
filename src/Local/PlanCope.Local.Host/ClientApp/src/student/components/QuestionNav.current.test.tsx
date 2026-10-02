// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { LocalExamBlock } from "../../shared/api-types";
import { QuestionNav } from "./QuestionNav";

Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });

function makeBlock(id: string): LocalExamBlock {
  return {
    id,
    localExamVersionId: "exam-v1",
    remoteBlockId: id,
    orderIndex: 0,
    blockType: 2,
    configJson: JSON.stringify({ question: "Pregunta", options: [] }),
    validationJson: JSON.stringify({ required: false })
  };
}

describe("QuestionNav current question", () => {
  let root: Root | undefined;

  afterEach(() => {
    if (root) {
      act(() => root?.unmount());
      root = undefined;
    }
    vi.unstubAllGlobals();
    document.body.replaceChildren();
  });

  it("marks the question intersecting the reading line as current", () => {
    let observerCallback: IntersectionObserverCallback | undefined;
    const observeSpy = vi.fn();
    const disconnectSpy = vi.fn();
    class MockIntersectionObserver {
      constructor(callback: IntersectionObserverCallback) {
        observerCallback = callback;
      }

      observe(target: Element) {
        observeSpy(target);
      }

      disconnect() {
        disconnectSpy();
      }
    }
    vi.stubGlobal("IntersectionObserver", MockIntersectionObserver);

    for (const id of ["question-1", "question-2"]) {
      const question = document.createElement("div");
      question.id = id;
      document.body.append(question);
    }
    const container = document.createElement("div");
    document.body.append(container);
    root = createRoot(container);
    act(() => {
      root?.render(<QuestionNav blocks={[makeBlock("question-1"), makeBlock("question-2")]} answers={{}} />);
    });

    expect(observeSpy).toHaveBeenCalledTimes(2);
    expect(container.querySelector('[data-nav-block="question-1"] button')?.getAttribute("aria-current")).toBeNull();
    const target = document.getElementById("question-2");
    expect(target).not.toBeNull();

    act(() => {
      observerCallback?.([
        { isIntersecting: true, target: target! } as unknown as IntersectionObserverEntry
      ], {} as IntersectionObserver);
    });

    expect(container.querySelector('[data-nav-block="question-2"] button')?.getAttribute("aria-current")).toBe("location");
    expect(container.querySelector('[data-nav-block="question-1"] button')?.getAttribute("aria-current")).toBeNull();
  });

  it("scrolls to the selected question and moves keyboard focus into its answer", () => {
    const question = document.createElement("div");
    question.id = "question-1";
    const answer = document.createElement("input");
    answer.type = "radio";
    question.append(answer);
    question.scrollIntoView = vi.fn();
    document.body.append(question);

    const container = document.createElement("div");
    document.body.append(container);
    root = createRoot(container);
    act(() => {
      root?.render(<QuestionNav blocks={[makeBlock("question-1")]} answers={{}} />);
    });

    act(() => {
      container.querySelector(".student-question-nav-item")?.dispatchEvent(
        new MouseEvent("click", { bubbles: true })
      );
    });

    expect(question.scrollIntoView).toHaveBeenCalledWith({ behavior: "smooth", block: "start" });
    expect(document.activeElement).toBe(answer);
  });
});
