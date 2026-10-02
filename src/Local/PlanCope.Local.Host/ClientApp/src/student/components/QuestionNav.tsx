import { useEffect, useMemo, useRef, useState } from "react";
import type { LocalExamBlock } from "../../shared/api-types";
import type { AnswerMap } from "../domain/examAnswers";
import { questionNumberFor } from "../domain/examAnswers";
import { hasAnswer, isAnswerBlock, parseValidation } from "../examBlocks";

export function scrollBlockIntoView(blockId: string): void {
  if (typeof document === "undefined") {
    return;
  }

  const element = document.getElementById(blockId);
  if (element) {
    element.scrollIntoView({ behavior: "smooth", block: "start" });
    const answer = element.querySelector<HTMLElement>(
      'input:not([type="hidden"]), textarea, button, [tabindex]:not([tabindex="-1"])'
    );
    answer?.focus({ preventScroll: true });
  }
}

type QuestionNavProps = {
  blocks: LocalExamBlock[];
  answers: AnswerMap;
};

export function QuestionNav({ blocks, answers }: QuestionNavProps) {
  const navRef = useRef<HTMLElement>(null);
  const [currentBlockId, setCurrentBlockId] = useState<string | null>(null);

  useEffect(() => {
    const nav = navRef.current;
    const header = document.querySelector<HTMLElement>(".student-header");
    if (!nav || !header) {
      return;
    }

    const updateHeaderHeight = () => {
      nav.style.setProperty("--student-header-height", `${header.getBoundingClientRect().height}px`);
    };
    updateHeaderHeight();
    const observer = typeof ResizeObserver === "undefined" ? null : new ResizeObserver(updateHeaderHeight);
    observer?.observe(header);
    window.addEventListener("resize", updateHeaderHeight);
    return () => {
      observer?.disconnect();
      window.removeEventListener("resize", updateHeaderHeight);
    };
  }, []);

  useEffect(() => {
    const answerBlocks = blocks.filter(isAnswerBlock);
    if (answerBlocks.length === 0 || typeof IntersectionObserver === "undefined") {
      setCurrentBlockId(null);
      return;
    }

    const observer = new IntersectionObserver(entries => {
      const current = entries.find(entry => entry.isIntersecting);
      if (current) {
        setCurrentBlockId(current.target.id);
      }
    }, {
      rootMargin: "-30% 0px -69% 0px",
      threshold: 0
    });

    for (const block of answerBlocks) {
      const element = document.getElementById(block.id);
      if (element) {
        observer.observe(element);
      }
    }

    return () => observer.disconnect();
  }, [blocks]);

  const navItems = useMemo(
    () => blocks.map((block, index) => ({ block, index })).filter(({ block }) => isAnswerBlock(block)),
    [blocks]
  );

  return (
    <nav
      className="student-question-nav"
      aria-label="Índice de preguntas"
      ref={navRef}
    >
      <p className="student-question-nav-title">Preguntas</p>
      <ol className="student-question-nav-list">
        {navItems.map(({ block, index }) => {
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
                aria-current={currentBlockId === block.id ? "location" : undefined}
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
