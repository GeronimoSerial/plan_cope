import { useMemo, useState } from "react";
import type { LocalExamBlock } from "../../shared/api-types";
import { ActionButton, MessageBar } from "../../shared/ui";
import type { AnswerMap } from "../domain/examAnswers";
import { questionNumberFor } from "../domain/examAnswers";
import { hasAnswer, isAnswerBlock, parseValidation } from "../examBlocks";
import { ExamBlock } from "./ExamBlock";
import { QuestionNav } from "./QuestionNav";
import { SubmitConfirmDialog } from "./SubmitConfirmDialog";

type ExamTakingPanelProps = {
  blocks: LocalExamBlock[];
  answers: AnswerMap;
  missingRequired: Set<string>;
  isBusy: boolean;
  status: string;
  sessionStatus?: string;
  error: string;
  studentName?: string | null;
  onAnswerChange: (blockId: string, value: string) => void;
  onSave: () => void;
  onSubmit: () => void;
};

export function ExamTakingPanel({
  blocks,
  answers,
  missingRequired,
  isBusy,
  status,
  sessionStatus = "active",
  error,
  studentName,
  onAnswerChange,
  onSave,
  onSubmit
}: ExamTakingPanelProps) {
  const [isSubmitOpen, setIsSubmitOpen] = useState(false);

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
  const paused = sessionStatus === "paused";
  const closed = sessionStatus === "closed";
  const statusTone: "success" | "warning" | "info" = status === "Respuestas guardadas."
    ? "success"
    : status.includes("Pendiente") || status.includes("Sin conexión") ? "warning" : "info";

  const handleConfirmSubmit = () => {
    setIsSubmitOpen(false);
    onSubmit();
  };

  return (
    <section className="student-exam">
      {paused && <p className="student-session-notice" role="status">La sesión está pausada por el docente. Tus respuestas están guardadas.</p>}
      {closed && <p className="student-session-notice" role="status">El docente cerró la sesión. Tu examen fue entregado.</p>}
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
        <fieldset className="student-question-lock" disabled={isBusy || paused || closed}>
        <div className="student-questions">
          {blocks.map((block, index) => {
            return (
              <div
                key={block.id}
                id={block.id}
                data-block-id={block.id}
                data-state="rendered"
                className="student-question-slot"
                tabIndex={-1}
              >
                <ExamBlock
                  block={block}
                  number={questionNumberFor(blocks, index)}
                  value={answers[block.id] ?? ""}
                  isMissing={missingRequired.has(block.id)}
                  onChange={value => onAnswerChange(block.id, value)}
                />
              </div>
            );
          })}
        </div>
        </fieldset>
      </div>

      <div className="student-actions-bar">
        <div className="student-actions">
          <ActionButton variant="secondary" disabled={isBusy || paused || closed} onClick={onSave}>
            {isBusy ? "Guardando…" : "Guardar respuestas"}
          </ActionButton>
          <ActionButton disabled={isBusy || paused || closed} onClick={() => setIsSubmitOpen(true)}>
            Enviar examen
          </ActionButton>
        </div>
      </div>

      {(status || error) && (
        <div className="student-exam-status">
          {status && <MessageBar tone={statusTone}>{status}</MessageBar>}
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
