"use client";

import { useSortable } from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import { questionTypeLabels, type Question } from "../../_lib/schema/exam";
import { QuestionEditor } from "./question-editor";
import { Button } from "../ui/button";

interface QuestionCardProps {
  question: Question;
  index: number;
  total: number;
  errors: Record<string, string>;
  onChange: (next: Question) => void;
  onRemove: () => void;
  onMoveUp: () => void;
  onMoveDown: () => void;
}

export function QuestionCard({
  question,
  index,
  total,
  errors,
  onChange,
  onRemove,
  onMoveUp,
  onMoveDown
}: QuestionCardProps) {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({ id: question.id });

  const style = {
    transform: CSS.Transform.toString(transform),
    transition,
    opacity: isDragging ? 0.6 : 1
  };

  return (
    <article className="question-card" ref={setNodeRef} style={style} aria-label={`Pregunta ${index + 1}`}>
      <div className="question-card__head">
        <button
          type="button"
          className="drag-handle"
          aria-label={`Arrastrar para reordenar la pregunta ${index + 1}`}
          {...attributes}
          {...listeners}
        >
          ⠿
        </button>
        <span className="question-card__title">
          {index + 1}. {questionTypeLabels[question.type]}
        </span>
        <span className="question-card__order">
          <Button
            variant="secondary"
            size="sm"
            onClick={onMoveUp}
            disabled={index === 0}
            aria-label={`Mover la pregunta ${index + 1} hacia arriba`}
          >
            ↑
          </Button>
          <Button
            variant="secondary"
            size="sm"
            onClick={onMoveDown}
            disabled={index === total - 1}
            aria-label={`Mover la pregunta ${index + 1} hacia abajo`}
          >
            ↓
          </Button>
          <Button variant="danger" size="sm" onClick={onRemove} aria-label={`Eliminar la pregunta ${index + 1}`}>
            Eliminar
          </Button>
        </span>
      </div>
      <div className="question-card__body">
        <QuestionEditor question={question} errors={errors} onChange={onChange} />
      </div>
    </article>
  );
}
