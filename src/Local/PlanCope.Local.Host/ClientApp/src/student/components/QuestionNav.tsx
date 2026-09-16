import { useEffect, useMemo, useRef, useState } from "react";
import type { LocalExamBlock } from "../../shared/api-types";
import type { AnswerMap } from "../domain/examAnswers";
import { questionNumberFor } from "../domain/examAnswers";
import { hasAnswer, isAnswerBlock, parseValidation } from "../examBlocks";

// Windowing constants shared between the nav and the questions list. Every item
// keeps a wrapper element with a reserved height so the total scroll height (and
// therefore scroll position) stays stable when real content is swapped for a
// lightweight placeholder.
export const DEFAULT_VIEWPORT_HEIGHT = 800;
export const VIEWPORT_BUFFER_VIEWPORTS = 2;
export const RESERVED_QUESTION_HEIGHT = 160;
export const QUESTION_GAP = 12;
export const RESERVED_NAV_ITEM_HEIGHT = 38;
export const NAV_GAP = 6;

export type WindowBounds = { start: number; end: number };

export function computeWindowBounds({
  scrollTop,
  viewportHeight,
  itemCount,
  itemStep,
  buffer
}: {
  scrollTop: number;
  viewportHeight: number;
  itemCount: number;
  itemStep: number;
  buffer?: number;
}): WindowBounds {
  if (itemCount <= 0 || itemStep <= 0) {
    return { start: 0, end: -1 };
  }

  const bufferPx = buffer ?? VIEWPORT_BUFFER_VIEWPORTS * viewportHeight;
  const start = Math.max(0, Math.floor((scrollTop - bufferPx) / itemStep));
  const end = Math.min(itemCount - 1, Math.ceil((scrollTop + viewportHeight + bufferPx) / itemStep));
  return { start, end };
}

export function scrollBlockIntoView(blockId: string): void {
  if (typeof document === "undefined") {
    return;
  }

  const element = document.getElementById(blockId);
  if (element) {
    element.scrollIntoView({ behavior: "smooth", block: "start" });
  }
}

type QuestionNavProps = {
  blocks: LocalExamBlock[];
  answers: AnswerMap;
};

export function QuestionNav({ blocks, answers }: QuestionNavProps) {
  const navRef = useRef<HTMLElement>(null);
  const [scrollTop, setScrollTop] = useState(0);
  const [viewportHeight, setViewportHeight] = useState(DEFAULT_VIEWPORT_HEIGHT);

  useEffect(() => {
    const nav = navRef.current;
    if (nav) {
      setViewportHeight(nav.clientHeight || DEFAULT_VIEWPORT_HEIGHT);
    }
  }, []);

  const navItems = useMemo(
    () => blocks.map((block, index) => ({ block, index })).filter(({ block }) => isAnswerBlock(block)),
    [blocks]
  );

  const { start, end } = useMemo(
    () =>
      computeWindowBounds({
        scrollTop,
        viewportHeight,
        itemCount: navItems.length,
        itemStep: RESERVED_NAV_ITEM_HEIGHT + NAV_GAP
      }),
    [scrollTop, viewportHeight, navItems.length]
  );

  return (
    <nav
      className="student-question-nav"
      aria-label="Índice de preguntas"
      ref={navRef}
      onScroll={event => setScrollTop(event.currentTarget.scrollTop)}
    >
      <p className="student-question-nav-title">Preguntas</p>
      <ol className="student-question-nav-list">
        {navItems.map(({ block, index }, itemIndex) => {
          if (itemIndex < start || itemIndex > end) {
            return (
              <li
                key={block.id}
                className="student-question-nav-slot"
                style={{ height: RESERVED_NAV_ITEM_HEIGHT }}
                data-nav-block={block.id}
                data-state="placeholder"
              />
            );
          }

          const number = questionNumberFor(blocks, index);
          const isAnswered = hasAnswer(answers[block.id]);
          const isRequired = parseValidation(block).required === true;
          const isMissing = isRequired && !isAnswered;
          const statusClass = isMissing
            ? "student-nav-missing"
            : isAnswered
              ? "student-nav-answered"
              : "student-nav-pending";
          const statusLabel = isMissing ? "Falta responder" : isAnswered ? "Respondida" : "Pendiente";

          return (
            <li key={block.id} data-nav-block={block.id} data-state="rendered">
              <button
                type="button"
                className={`student-question-nav-item ${statusClass}`}
                aria-label={`Pregunta ${number}: ${statusLabel}`}
                onClick={() => scrollBlockIntoView(block.id)}
              >
                <span className="student-nav-number">{number}</span>
                <span className="student-nav-status">{statusLabel}</span>
              </button>
            </li>
          );
        })}
      </ol>
    </nav>
  );
}
