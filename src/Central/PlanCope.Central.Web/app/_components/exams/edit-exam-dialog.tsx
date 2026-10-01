"use client";

import { useState, type FormEvent } from "react";
import { useRouter } from "next/navigation";
import { toast } from "sonner";
import { callCentral } from "../../_lib/api/client";
import { getErrorMessage } from "../../_lib/json";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle
} from "@/components/ui/dialog";
import { Field, FieldError, FieldGroup, FieldLabel } from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import type { ExamSummary, UpdateExamRequest } from "../../_lib/contracts";

interface EditExamButtonProps {
  exam: ExamSummary;
  canEditExams: boolean;
}

interface EditExamErrors {
  title?: string;
  code?: string;
  form?: string;
}

function mapUpdateExamError(error: unknown): EditExamErrors {
  const message = getErrorMessage(error, "").toLowerCase();
  if (message.includes("code") || message.includes("código")) {
    return { code: "El código no se puede modificar." };
  }
  if (message.includes("title") || message.includes("título")) {
    return { title: "Revisá el título del examen." };
  }
  return { form: "No se pudieron guardar los datos. Revisá los campos e intentá de nuevo." };
}

// "Editar datos" del examen: titulo, nivel, area y materia. El codigo es inmutable y se muestra
// solo de lectura; el API devuelve 400 bajo la clave "code" si llegara a cambiar.
export function EditExamButton({ exam, canEditExams }: EditExamButtonProps) {
  const router = useRouter();
  const [open, setOpen] = useState(false);
  const [title, setTitle] = useState(exam.title);
  const [level, setLevel] = useState(exam.level ?? "");
  const [area, setArea] = useState(exam.area ?? "");
  const [subject, setSubject] = useState(exam.subject ?? "");
  const [errors, setErrors] = useState<EditExamErrors>({});
  const [pending, setPending] = useState(false);

  if (!canEditExams) return null;

  function resetForm() {
    setTitle(exam.title);
    setLevel(exam.level ?? "");
    setArea(exam.area ?? "");
    setSubject(exam.subject ?? "");
    setErrors({});
    setPending(false);
  }

  async function submit() {
    const trimmedTitle = title.trim();
    if (!trimmedTitle) {
      setErrors({ title: "El título es requerido." });
      return;
    }
    setErrors({});
    setPending(true);
    const body: UpdateExamRequest = {
      title: trimmedTitle,
      level: level.trim() || null,
      area: area.trim() || null,
      subject: subject.trim() || null
    };
    try {
      await callCentral<ExamSummary>(`exams/${encodeURIComponent(exam.id)}`, {
        method: "PUT",
        body: JSON.stringify(body)
      });
      toast.success("Datos del examen actualizados.");
      setOpen(false);
      resetForm();
      router.refresh();
    } catch (error) {
      setErrors(mapUpdateExamError(error));
      setPending(false);
    }
  }

  function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    void submit();
  }

  return (
    <>
      <Button type="button" variant="outline" onClick={() => setOpen(true)}>
        Editar datos
      </Button>
      <Dialog
        open={open}
        onOpenChange={next => {
          setOpen(next);
          if (!next) {
            resetForm();
          }
        }}
      >
        <DialogContent>
          <form onSubmit={onSubmit} noValidate className="grid gap-4">
            <DialogHeader>
              <DialogTitle>Editar datos</DialogTitle>
              <DialogDescription>Datos generales del examen. El código no se puede cambiar.</DialogDescription>
            </DialogHeader>

            <FieldGroup className="gap-4">
              <Field>
                <FieldLabel htmlFor="edit-exam-code">Código</FieldLabel>
                <Input id="edit-exam-code" value={exam.code} readOnly disabled />
              </Field>

              <Field data-invalid={errors.title ? true : undefined}>
                <FieldLabel htmlFor="edit-exam-title">Título</FieldLabel>
                <Input
                  id="edit-exam-title"
                  value={title}
                  aria-invalid={errors.title ? true : undefined}
                  onChange={event => {
                    setTitle(event.target.value);
                    if (errors.title) {
                      setErrors(previous => ({ ...previous, title: undefined }));
                    }
                  }}
                />
                {errors.title && <FieldError>{errors.title}</FieldError>}
              </Field>

              <div className="grid gap-4 sm:grid-cols-3">
                <Field>
                  <FieldLabel htmlFor="edit-exam-level">Nivel / Curso</FieldLabel>
                  <Input id="edit-exam-level" value={level} onChange={event => setLevel(event.target.value)} />
                </Field>
                <Field>
                  <FieldLabel htmlFor="edit-exam-area">Área</FieldLabel>
                  <Input id="edit-exam-area" value={area} onChange={event => setArea(event.target.value)} />
                </Field>
                <Field>
                  <FieldLabel htmlFor="edit-exam-subject">Materia</FieldLabel>
                  <Input id="edit-exam-subject" value={subject} onChange={event => setSubject(event.target.value)} />
                </Field>
              </div>
            </FieldGroup>

            {(errors.code || errors.form) && (
              <p role="alert" className="text-sm text-destructive">
                {errors.code ?? errors.form}
              </p>
            )}

            <DialogFooter>
              <Button type="button" variant="outline" onClick={() => setOpen(false)} disabled={pending}>
                Cancelar
              </Button>
              <Button type="submit" disabled={pending}>
                {pending ? "Guardando…" : "Guardar"}
              </Button>
            </DialogFooter>
          </form>
        </DialogContent>
      </Dialog>
    </>
  );
}
