import type { LocalExamBlock } from "../../shared/api-types";
import type { AnswerMap } from "../domain/examAnswers";
import { questionNumberFor } from "../domain/examAnswers";
import { hasAnswer, isAnswerBlock, parseValidation } from "../examBlocks";

type QuestionNavProps = {
  blocks: LocalExamBlock[];
  answers: AnswerMap;
};

export function QuestionNav({ blocks, answers }: QuestionNavProps) {
  const handleJump = (blockId: string) => {
    const element = document.getElementById(blockId);
    if (element) {
      element.scrollIntoView({ behavior: "smooth", block: "start" });
    }
  };

  return (
    <nav className="student-question-nav" aria-label="Indice de preguntas">
      <p className="student-question-nav-title">Preguntas</p>
      <ol className="student-question-nav-list">
        {blocks.map((block, index) => {
          if (!isAnswerBlock(block)) {
            return null;
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
          const statusLabel = isMissing ? "Falta" : isAnswered ? "Ok" : "—";

          return (
            <li key={block.id}>
              <button
                type="button"
                className={`student-question-nav-item ${statusClass}`}
                onClick={() => handleJump(block.id)}
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
