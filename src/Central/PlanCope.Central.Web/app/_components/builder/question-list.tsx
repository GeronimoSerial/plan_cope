"use client";

import { useState } from "react";
import {
  DndContext,
  closestCenter,
  KeyboardSensor,
  PointerSensor,
  useSensor,
  useSensors,
  type DragEndEvent
} from "@dnd-kit/core";
import { SortableContext, sortableKeyboardCoordinates, verticalListSortingStrategy } from "@dnd-kit/sortable";
import { questionTypes, questionTypeLabels, type Question, type QuestionType } from "../../_lib/schema/exam";
import { QuestionCard } from "./question-card";
import { Button } from "../ui/button";
import { EmptyState } from "../ui/empty-state";

interface QuestionListProps {
  questions: Question[];
  errorsByIndex: (index: number) => Record<string, string>;
  onReorder: (activeId: string, overId: string) => void;
  onUpdate: (index: number, next: Question) => void;
  onRemove: (index: number) => void;
  onMove: (index: number, direction: -1 | 1) => void;
  onAdd: (type: QuestionType) => void;
}

export function QuestionList({
  questions,
  errorsByIndex,
  onReorder,
  onUpdate,
  onRemove,
  onMove,
  onAdd
}: QuestionListProps) {
  const [newType, setNewType] = useState<QuestionType>("single_choice");
  const sensors = useSensors(
    useSensor(PointerSensor),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates })
  );

  function handleDragEnd(event: DragEndEvent) {
    const { active, over } = event;
    if (over && active.id !== over.id) {
      onReorder(String(active.id), String(over.id));
    }
  }

  return (
    <div className="stack">
      <div className="card">
        <div className="card__body row row--between">
          <div className="row" style={{ alignItems: "center" }}>
            <label htmlFor="new-question-type" className="field-label">
              Agregar pregunta
            </label>
            <select
              id="new-question-type"
              value={newType}
              onChange={event => setNewType(event.target.value as QuestionType)}
              style={{ width: "auto" }}
            >
              {questionTypes.map(type => (
                <option key={type} value={type}>
                  {questionTypeLabels[type]}
                </option>
              ))}
            </select>
          </div>
          <Button onClick={() => onAdd(newType)}>+ Agregar</Button>
        </div>
      </div>

      {questions.length === 0 ? (
        <EmptyState
          icon="❓"
          title="Todavía no hay preguntas"
          description="Elegí un tipo de pregunta y agregala para empezar a construir el examen."
        />
      ) : (
        <DndContext sensors={sensors} collisionDetection={closestCenter} onDragEnd={handleDragEnd}>
          <SortableContext items={questions.map(question => question.id)} strategy={verticalListSortingStrategy}>
            <div className="stack">
              {questions.map((question, index) => (
                <QuestionCard
                  key={question.id}
                  question={question}
                  index={index}
                  total={questions.length}
                  errors={errorsByIndex(index)}
                  onChange={next => onUpdate(index, next)}
                  onRemove={() => onRemove(index)}
                  onMoveUp={() => onMove(index, -1)}
                  onMoveDown={() => onMove(index, 1)}
                />
              ))}
            </div>
          </SortableContext>
        </DndContext>
      )}
    </div>
  );
}
