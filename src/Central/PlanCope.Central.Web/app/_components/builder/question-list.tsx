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
import { Button } from "@/components/ui/button";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { QuestionCard } from "./question-card";

interface QuestionListProps {
  versionId: string;
  questions: Question[];
  errors: Record<string, string>;
  disabled?: boolean;
  onReorder: (activeId: string, overId: string) => void;
  onMove: (id: string, direction: -1 | 1) => void;
  onUpdate: (id: string, next: Question) => void;
  onRemove: (id: string) => void;
  onDuplicate: (id: string) => void;
  onAdd: (type: QuestionType) => void;
}

export function QuestionList({
  versionId,
  questions,
  errors,
  disabled = false,
  onReorder,
  onMove,
  onUpdate,
  onRemove,
  onDuplicate,
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
    <div className="grid gap-4">
      {!disabled && (
        <div className="flex flex-col gap-2 rounded-xl border p-4 sm:flex-row sm:items-end sm:justify-between">
          <div className="grid gap-1.5 sm:max-w-xs sm:flex-1">
            <label htmlFor="new-question-type" className="text-sm font-medium">
              Agregar pregunta
            </label>
            <Select
              value={newType}
              onValueChange={value => setNewType(value as QuestionType)}
              items={questionTypeLabels}
            >
              <SelectTrigger id="new-question-type" className="w-full" aria-label="Tipo de pregunta nueva">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {questionTypes.map(type => (
                  <SelectItem key={type} value={type}>
                    {questionTypeLabels[type]}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <Button type="button" variant="secondary" onClick={() => onAdd(newType)}>
            Agregar pregunta
          </Button>
        </div>
      )}

      {questions.length === 0 ? (
        <p className="rounded-xl border py-10 text-center text-sm text-muted-foreground">
          Todavía no hay preguntas. Agregá la primera para empezar.
        </p>
      ) : (
        <DndContext sensors={sensors} collisionDetection={closestCenter} onDragEnd={handleDragEnd}>
          <SortableContext items={questions.map(question => question.id)} strategy={verticalListSortingStrategy}>
            <div className="grid gap-4">
              {questions.map((question, index) => (
                <QuestionCard
                  key={question.id}
                  question={question}
                  versionId={versionId}
                  index={index}
                  questionCount={questions.length}
                  errors={errors}
                  disabled={disabled}
                  onUpdate={onUpdate}
                  onMove={onMove}
                  onRemove={onRemove}
                  onDuplicate={onDuplicate}
                />
              ))}
            </div>
          </SortableContext>
        </DndContext>
      )}
    </div>
  );
}
