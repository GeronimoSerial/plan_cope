"use client";

import { memo, useMemo, useState } from "react";
import { useSortable } from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import { ArrowDownIcon, ArrowUpIcon, CopyIcon, GripVerticalIcon, Trash2Icon } from "lucide-react";
import { questionTypes, type Question, type QuestionType } from "../../_lib/schema/exam";
import { blankQuestion } from "../../_lib/schema/mappers";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Field, FieldLabel } from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { TermHint } from "../help/term-hint";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle
} from "@/components/ui/alert-dialog";
import { QuestionEditor } from "./question-editor";

const compactQuestionTypeLabels: Record<QuestionType, string> = {
  single_choice: "Única",
  multiple_choice: "Múltiple",
  true_false: "Verdadero o falso"
};

interface QuestionCardProps {
  question: Question;
  versionId: string;
  index: number;
  questionCount: number;
  errors: Record<string, string>;
  disabled?: boolean;
  onUpdate: (id: string, next: Question) => void;
  onMove: (id: string, direction: -1 | 1) => void;
  onRemove: (id: string) => void;
  onDuplicate: (id: string) => void;
}

function questionHasContent(question: Question): boolean {
  if ((question.prompt ?? "").trim().length > 0) {
    return true;
  }
  if ((question.help ?? "").trim().length > 0) {
    return true;
  }
  if ((question.type === "single_choice" || question.type === "multiple_choice") &&
    question.options.some(option => option.label.trim().length > 0)) {
    return true;
  }
  return false;
}

function sliceErrors(errors: Record<string, string>, index: number): Record<string, string> {
  const prefix = `questions.${index}.`;
  const result: Record<string, string> = {};
  for (const [key, message] of Object.entries(errors)) {
    if (key.startsWith(prefix)) {
      result[key.slice(prefix.length)] = message;
    }
  }
  return result;
}

export const QuestionCard = memo(function QuestionCard({
  question,
  versionId,
  index,
  questionCount,
  errors,
  disabled = false,
  onUpdate,
  onMove,
  onRemove,
  onDuplicate
}: QuestionCardProps) {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({
    id: question.id,
    disabled
  });
  const [confirmOpen, setConfirmOpen] = useState(false);
  const ownErrors = useMemo(() => sliceErrors(errors, index), [errors, index]);

  const style = {
    transform: CSS.Transform.toString(transform),
    transition,
    opacity: isDragging ? 0.6 : 1
  };

  function requestRemove() {
    if (questionHasContent(question)) {
      setConfirmOpen(true);
    } else {
      onRemove(question.id);
    }
  }

  function changeType(type: QuestionType) {
    if (type === question.type) return;
    const fresh = blankQuestion(type);
    onUpdate(question.id, {
      ...fresh,
      id: question.id,
      prompt: question.prompt ?? "",
      help: question.help,
      required: question.required,
      score: question.score,
      ...(question.imageAssetId ? { imageAssetId: question.imageAssetId } : {})
    } as Question);
  }

  return (
    <div ref={setNodeRef} style={style}>
      <Card data-size="sm" className="gap-0">
        <CardHeader className="flex flex-row flex-wrap items-center gap-x-2 gap-y-1.5 px-3 py-2">
          <div className="flex min-w-0 flex-1 flex-wrap items-center gap-2">
            <Button
              type="button"
              variant="ghost"
              size="icon-sm"
              aria-label="Reordenar"
              className="cursor-grab touch-none"
              disabled={disabled}
              {...attributes}
              {...listeners}
            >
              <GripVerticalIcon />
            </Button>
            <CardTitle className="shrink-0 text-sm">{index + 1}.</CardTitle>
            <Select value={question.type} onValueChange={value => changeType(value as QuestionType)} disabled={disabled} items={compactQuestionTypeLabels}>
              <SelectTrigger id={`question-${question.id}-type`} className="h-8 w-36" aria-label={`Tipo de pregunta ${index + 1}`}>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {questionTypes.map(type => <SelectItem key={type} value={type}>{compactQuestionTypeLabels[type]}</SelectItem>)}
              </SelectContent>
            </Select>
            <Field className="w-16 gap-1">
              <div className="flex items-center gap-1">
                <FieldLabel htmlFor={`question-${question.id}-score`} className="text-xs">Pts</FieldLabel>
                <TermHint term="puntos" />
              </div>
              <Input id={`question-${question.id}-score`} className="h-8 px-2" type="number" min={0} step={1} value={question.score} disabled={disabled} aria-label={`Pts, pregunta ${index + 1}`} onChange={event => onUpdate(question.id, { ...question, score: Number(event.target.value) } as Question)} />
            </Field>
            <div className="flex items-center gap-1.5">
              <Checkbox id={`question-${question.id}-required`} checked={question.required} disabled={disabled} onCheckedChange={checked => onUpdate(question.id, { ...question, required: checked === true } as Question)} />
              <Label htmlFor={`question-${question.id}-required`} className="whitespace-nowrap text-sm font-normal">Obligatoria</Label>
            </div>
          </div>
          <div className="ml-auto flex items-center gap-0.5">
            <Button type="button" variant="ghost" size="icon-sm" title="Mover pregunta hacia arriba" aria-label={`Mover pregunta ${index + 1} hacia arriba`} disabled={disabled || index === 0} onClick={() => onMove(question.id, -1)}><ArrowUpIcon /></Button>
            <Button type="button" variant="ghost" size="icon-sm" title="Mover pregunta hacia abajo" aria-label={`Mover pregunta ${index + 1} hacia abajo`} disabled={disabled || index === questionCount - 1} onClick={() => onMove(question.id, 1)}><ArrowDownIcon /></Button>
            {!disabled && <Button type="button" variant="ghost" size="icon-sm" title="Duplicar pregunta" aria-label={`Duplicar pregunta ${index + 1}`} onClick={() => onDuplicate(question.id)}><CopyIcon /></Button>}
            {!disabled && <Button type="button" variant="ghost" size="icon-sm" title="Eliminar pregunta" aria-label={`Eliminar pregunta ${index + 1}`} onClick={requestRemove}><Trash2Icon /></Button>}
          </div>
        </CardHeader>
        <CardContent className="px-3 pb-3 pt-0">
          <QuestionEditor
            question={question}
            versionId={versionId}
            errors={ownErrors}
            disabled={disabled}
            onChange={next => onUpdate(question.id, next)}
          />
        </CardContent>
      </Card>

      <AlertDialog open={confirmOpen} onOpenChange={setConfirmOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Eliminar pregunta</AlertDialogTitle>
            <AlertDialogDescription>
              La pregunta {index + 1} tiene contenido. ¿Querés eliminarla igual?
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancelar</AlertDialogCancel>
            <AlertDialogAction
              variant="destructive"
              onClick={() => {
                setConfirmOpen(false);
                onRemove(question.id);
              }}
            >
              Eliminar
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
});
