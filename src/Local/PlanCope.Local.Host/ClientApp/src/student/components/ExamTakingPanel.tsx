import { useEffect, useMemo, useState } from "react";
import type { LocalExamBlock } from "../../shared/api-types";
import { ActionButton } from "../../shared/ui";
import type { AnswerMap } from "../domain/examAnswers";
import { questionNumberFor } from "../domain/examAnswers";
import { hasAnswer, isAnswerBlock, parseValidation } from "../examBlocks";
import { ExamBlock } from "./ExamBlock";
import {
  computeWindowBounds,
  DEFAULT_VIEWPORT_HEIGHT,
  QUESTION_GAP,
  QuestionNav,
  RESERVED_QUESTION_HEIGHT
} from "./QuestionNav";
import { SubmitConfirmDialog } from "./SubmitConfirmDialog";

type ExamTakingPanelProps = {
  blocks: LocalExamBlock[];
  answers: AnswerMap;
  missingRequired: Set<string>;
  isBusy: boolean;
  status: string;
  error: string;
  studentName?: string | null;
  onAnswerChange: (blockId: string, value: string) => void;
  onSave: () => void;
  onSubmit: () => void;
};

function usePageScrollWindow() {
  const [scrollY, setScrollY] = useState(() =>
    typeof window === "undefined" ? 0 : (window.scrollY || 0)
  );
  const [viewportHeight, setViewportHeight] = useState(() =>
    typeof window === "undefined" ? DEFAULT_VIEWPORT_HEIGHT : (window.innerHeight || DEFAULT_VIEWPORT_HEIGHT)
  );

  useEffect(() => {
    if (typeof window === "undefined") {
      return;
    }

    const onScroll = () => setScrollY(window.scrollY || 0);
    const onResize = () => setViewportHeight(window.innerHeight || DEFAULT_VIEWPORT_HEIGHT);
    onScroll();
    onResize();
    window.addEventListener("scroll", onScroll, { passive: true });
    window.addEventListener("resize", onResize);
    return () => {
      window.removeEventListener("scroll", onScroll);
      window.removeEventListener("resize", onResize);
    };
  }, []);

  return { scrollY, viewportHeight };
}

export function ExamTakingPanel({
  blocks,
  answers,
  missingRequired,
  isBusy,
  status,
  error,
  studentName,
  onAnswerChange,
  onSave,
  onSubmit
}: ExamTakingPanelProps) {
  const [isSubmitOpen, setIsSubmitOpen] = useState(false);

  const { scrollY, viewportHeight } = usePageScrollWindow();

  const { start, end } = useMemo(
    () =>
      computeWindowBounds({
        scrollTop: scrollY,
        viewportHeight,
        itemCount: blocks.length,
        itemStep: RESERVED_QUESTION_HEIGHT + QUESTION_GAP
      }),
    [scrollY, viewportHeight, blocks.length]
  );

  const { total, answered, requiredMissing } = useMemo(() => {
    const answerBlocks = blocks.filter(isAnswerBlock);
    const answeredCount = answerBlocks.filter(block => hasAnswer(answers[block.id])).length;
    const missingCount = answerBlocks.filter(
      block => parseValidation(block).required === true && !hasAnswer(answers[block.id])
    ).length;
    return {
      total: answerBlocks.length,
      answered: answeredCount,
      requiredMissing: missingCount
    };
  }, [blocks, answers]);

  const completion = total === 0 ? 0 : Math.round((answered / total) * 100);

  const handleConfirmSubmit = () => {
    setIsSubmitOpen(false);
    onSubmit();
  };

  return (
    <section className="student-exam">
      <header className="student-exam-header">
        <h2>Respondé el examen</h2>
        {studentName && <p className="student-exam-identity">Estudiante: <strong>{studentName}</strong></p>}
        <div className="student-exam-progress">
          <div
            className="student-progress-bar"
            role="progressbar"
            aria-label="Progreso del examen"
            aria-valuemin={0}
            aria-valuemax={100}
            aria-valuenow={completion}
            aria-valuetext={`${completion}% completado`}
          >
            <span style={{ width: `${completion}%` }} />
          </div>
          <p className="student-progress-label">
            <strong>{answered}</strong> / {total} respondidas
            {requiredMissing > 0 && (
              <>
                {" · "}
                <strong>{requiredMissing}</strong> obligatorias sin responder
              </>
            )}
          </p>
        </div>
        <p className="student-legend">
          <span className="student-required-mark">*</span> indica una pregunta obligatoria.
        </p>
      </header>

      <div className="student-exam-body">
        <QuestionNav blocks={blocks} answers={answers} />
        <div className="student-questions">
          {blocks.map((block, index) => {
            const isRendered = index >= start && index <= end;
            return (
              <div
                key={block.id}
                id={block.id}
                data-block-id={block.id}
                data-state={isRendered ? "rendered" : "placeholder"}
                className="student-question-slot"
                style={{ minHeight: RESERVED_QUESTION_HEIGHT, scrollMarginTop: 16 }}
              >
                {isRendered ? (
                  <ExamBlock
                    block={block}
                    number={questionNumberFor(blocks, index)}
                    value={answers[block.id] ?? ""}
                    isMissing={missingRequired.has(block.id)}
                    onChange={value => onAnswerChange(block.id, value)}
                  />
                ) : (
                  <div
                    className="student-question-placeholder"
                    style={{ height: RESERVED_QUESTION_HEIGHT }}
                  />
                )}
              </div>
            );
          })}
        </div>
      </div>

      <div className="student-actions-bar">
        <div className="student-actions">
          <ActionButton variant="secondary" disabled={isBusy} onClick={onSave}>
            {isBusy ? "Guardando…" : "Guardar respuestas"}
          </ActionButton>
          <ActionButton disabled={isBusy} onClick={() => setIsSubmitOpen(true)}>
            Enviar examen
          </ActionButton>
        </div>
      </div>

      {(status || error) && (
        <div className="student-exam-status">
          {status && <p className="builder-status">{status}</p>}
          {error && <p className="error-banner">{error}</p>}
        </div>
      )}

      {isSubmitOpen && (
        <SubmitConfirmDialog
          answered={answered}
          total={total}
          missingRequiredCount={requiredMissing}
          onConfirm={handleConfirmSubmit}
          onCancel={() => setIsSubmitOpen(false)}
        />
      )}
    </section>
  );
}
