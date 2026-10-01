"use client";

import { memo, useMemo, useState } from "react";
import { useSortable } from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import { CopyIcon, GripVerticalIcon, MoreHorizontal, Trash2Icon } from "lucide-react";
import { questionTypeLabels, type Question } from "../../_lib/schema/exam";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger
} from "@/components/ui/dropdown-menu";
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

interface QuestionCardProps {
  question: Question;
  versionId: string;
  index: number;
  errors: Record<string, string>;
  disabled?: boolean;
  onUpdate: (id: string, next: Question) => void;
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
  errors,
  disabled = false,
  onUpdate,
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

  return (
    <div ref={setNodeRef} style={style}>
      <Card data-size="sm">
        <CardHeader className="flex flex-row items-center justify-between gap-2">
          <div className="flex min-w-0 items-center gap-2">
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
            <CardTitle className="truncate">
              {index + 1}. {questionTypeLabels[question.type]}
            </CardTitle>
          </div>
          {!disabled && (
            <DropdownMenu>
              <DropdownMenuTrigger render={<Button variant="ghost" size="icon-sm" aria-label="Acciones de la pregunta" />}>
                <MoreHorizontal />
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end">
                <DropdownMenuItem onClick={() => onDuplicate(question.id)}>
                  <CopyIcon data-icon="inline-start" />
                  Duplicar
                </DropdownMenuItem>
                <DropdownMenuItem variant="destructive" onClick={requestRemove}>
                  <Trash2Icon data-icon="inline-start" />
                  Eliminar
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          )}
        </CardHeader>
        <CardContent>
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
