"use client";

import { useState, type FormEvent } from "react";
import { useRouter } from "next/navigation";
import { toast } from "sonner";
import { Plus } from "lucide-react";
import { callCentral } from "../../_lib/api/client";
import { getErrorMessage } from "../../_lib/json";
import { validateCreateExam, type CreateExamErrors } from "../../_lib/exams/exam-state";
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
import type { ExamSummary } from "../../_lib/contracts";

function mapCreateExamError(error: unknown): string {
  const message = getErrorMessage(error, "");
  if (message.toLowerCase().includes("already exists")) {
    return "Ya existe un examen con ese código.";
  }
  return "No se pudo crear el examen. Revisá los datos e intentá de nuevo.";
}

export function CreateExamButton({ canEditExams }: { canEditExams: boolean }) {
  if (!canEditExams) return null;
  const router = useRouter();
  const [open, setOpen] = useState(false);
  const [code, setCode] = useState("");
  const [title, setTitle] = useState("");
  const [errors, setErrors] = useState<CreateExamErrors>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

  function resetForm() {
    setCode("");
    setTitle("");
    setErrors({});
    setFormError(null);
    setPending(false);
  }

  async function createExam() {
    setPending(true);
    setFormError(null);
    try {
      const created = await callCentral<ExamSummary>("exams", {
        method: "POST",
        body: JSON.stringify({
          code: code.trim(),
          title: title.trim(),
          description: null,
          level: null,
          area: null,
          subject: null
        })
      });
      toast.success("Examen creado.");
      setOpen(false);
      resetForm();
      const target = created.initialVersionId
        ? `/exams/${created.id}/versions/${created.initialVersionId}/builder`
        : `/exams/${created.id}`;
      router.push(target);
    } catch (error) {
      setFormError(mapCreateExamError(error));
      setPending(false);
    }
  }

  function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const nextErrors = validateCreateExam({ code, title });
    setErrors(nextErrors);
    if (nextErrors.code || nextErrors.title) {
      return;
    }
    void createExam();
  }

  return (
    <>
      <Button onClick={() => setOpen(true)}>
        <Plus data-icon="inline-start" />
        Nuevo examen
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
              <DialogTitle>Nuevo examen</DialogTitle>
              <DialogDescription>Código y título. Después agregás las preguntas en el builder.</DialogDescription>
            </DialogHeader>

            <FieldGroup className="gap-4">
              <Field data-invalid={errors.code ? true : undefined}>
                <FieldLabel htmlFor="new-exam-code">Código</FieldLabel>
                <Input
                  id="new-exam-code"
                  placeholder="Ej. MAT-2026-01"
                  autoComplete="off"
                  aria-invalid={errors.code ? true : undefined}
                  value={code}
                  onChange={event => {
                    setCode(event.target.value);
                    if (errors.code) {
                      setErrors(previous => ({ ...previous, code: undefined }));
                    }
                  }}
                />
                {errors.code && <FieldError>{errors.code}</FieldError>}
              </Field>

              <Field data-invalid={errors.title ? true : undefined}>
                <FieldLabel htmlFor="new-exam-title">Título</FieldLabel>
                <Input
                  id="new-exam-title"
                  placeholder="Ej. Matemática · Primer Año"
                  aria-invalid={errors.title ? true : undefined}
                  value={title}
                  onChange={event => {
                    setTitle(event.target.value);
                    if (errors.title) {
                      setErrors(previous => ({ ...previous, title: undefined }));
                    }
                  }}
                />
                {errors.title && <FieldError>{errors.title}</FieldError>}
              </Field>
            </FieldGroup>

            {formError && (
              <p role="alert" className="text-sm text-destructive">
                {formError}
              </p>
            )}

            <DialogFooter>
              <Button type="button" variant="outline" onClick={() => setOpen(false)} disabled={pending}>
                Cancelar
              </Button>
              <Button type="submit" disabled={pending}>
                {pending ? "Creando…" : "Crear examen"}
              </Button>
            </DialogFooter>
          </form>
        </DialogContent>
      </Dialog>
    </>
  );
}
