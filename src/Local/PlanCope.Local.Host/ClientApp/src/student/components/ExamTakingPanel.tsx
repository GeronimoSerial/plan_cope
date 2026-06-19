import type { LocalExamBlock } from "../../shared/api-types";
import { ActionButton } from "../../shared/ui";
import type { AnswerMap } from "../domain/examAnswers";
import { questionNumberFor } from "../domain/examAnswers";
import { ExamBlock } from "./ExamBlock";

type ExamTakingPanelProps = {
  blocks: LocalExamBlock[];
  answers: AnswerMap;
  missingRequired: Set<string>;
  isBusy: boolean;
  status: string;
  error: string;
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
  error,
  onAnswerChange,
  onSave,
  onSubmit
}: ExamTakingPanelProps) {
  return (
    <section className="student-panel">
      <h2>Responder examen</h2>
      <div className="student-questions">
        {blocks.map((block, index) => (
          <ExamBlock
            key={block.id}
            block={block}
            number={questionNumberFor(blocks, index)}
            value={answers[block.id] ?? ""}
            isMissing={missingRequired.has(block.id)}
            onChange={value => onAnswerChange(block.id, value)}
          />
        ))}
      </div>
      <div className="student-actions">
        <ActionButton variant="secondary" disabled={isBusy} onClick={onSave}>
          Guardar respuestas
        </ActionButton>
        <ActionButton disabled={isBusy} onClick={onSubmit}>
          Enviar examen
        </ActionButton>
      </div>
      {status && <p className="student-status">{status}</p>}
      {error && <p className="student-error">{error}</p>}
    </section>
  );
}
