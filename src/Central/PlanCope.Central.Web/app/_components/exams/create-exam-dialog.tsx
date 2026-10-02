"use client";

import { useState, type FormEvent } from "react";
import { useRouter } from "next/navigation";
import { toast } from "sonner";
import { Plus } from "lucide-react";
import { callCentral } from "../../_lib/api/client";
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
import { areaOptions, courseOptions } from "../../_lib/exams/catalog";
import { GradeSectionPicker } from "../shared/grade-section-picker";

function mapCreateExamError(): string {
  return "No se pudo crear el examen. Revisá los datos e intentá de nuevo.";
}

export function CreateExamButton({ canEditExams }: { canEditExams: boolean }) {
  const router = useRouter();
  const [open, setOpen] = useState(false);
  const [title, setTitle] = useState("");
  const [courses, setCourses] = useState<string[]>([]);
  const [areaChoice, setAreaChoice] = useState("");
  const [customArea, setCustomArea] = useState("");
  const [errors, setErrors] = useState<CreateExamErrors>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

  if (!canEditExams) return null;

  function resetForm() {
    setTitle("");
    setCourses([]);
    setAreaChoice("");
    setCustomArea("");
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
          title: title.trim(),
          description: null,
          courses,
          area: areaChoice === "Otro" ? customArea.trim() : areaChoice || null,
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
    } catch {
      setFormError(mapCreateExamError());
      setPending(false);
    }
  }

  function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const nextErrors = validateCreateExam({ title });
    setErrors(nextErrors);
    if (nextErrors.title || courses.length === 0 || (areaChoice === "Otro" && !customArea.trim())) {
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
        <DialogContent className="grid-rows-[minmax(0,1fr)] overflow-hidden sm:max-w-2xl">
          <form onSubmit={onSubmit} noValidate className="grid min-h-0 max-h-full grid-rows-[auto_minmax(0,1fr)_auto] gap-4">
            <DialogHeader>
              <DialogTitle>Nuevo examen</DialogTitle>
              <DialogDescription>Título y curso. Después agregás las preguntas en el builder.</DialogDescription>
            </DialogHeader>

            <div className="min-h-0 overflow-y-auto overscroll-contain">
              <FieldGroup className="gap-4">
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
              <Field data-invalid={courses.length === 0 ? true : undefined}>
                <GradeSectionPicker mode="multi" grades={courseOptions.map(course => ({ value: course.key, label: course.label, group: course.level }))}
                  value={courses} onValueChange={next => setCourses(Array.isArray(next) ? next : [next])} showSection={false} disabled={pending}
                  className="rounded-md border p-3" />
                {courses.length === 0 && <FieldError>Seleccioná al menos un curso.</FieldError>}
              </Field>
              <Field>
                <FieldLabel htmlFor="new-exam-area">Área (opcional)</FieldLabel>
                <select id="new-exam-area" className="h-9 rounded-md border bg-background px-3 text-sm" value={areaChoice}
                  onChange={event => setAreaChoice(event.target.value)}>
                  <option value="">Seleccionar área</option>
                  {areaOptions.map(area => <option key={area} value={area}>{area}</option>)}
                </select>
              </Field>
              {areaChoice === "Otro" && <Field data-invalid={!customArea.trim() ? true : undefined}>
                <FieldLabel htmlFor="new-exam-custom-area">Área</FieldLabel>
                <Input id="new-exam-custom-area" value={customArea} onChange={event => setCustomArea(event.target.value)} />
                {!customArea.trim() && <FieldError>Ingresá el nombre del área.</FieldError>}
              </Field>}
              </FieldGroup>

              {formError && (
                <p role="alert" className="mt-4 text-sm text-destructive">
                  {formError}
                </p>
              )}
            </div>

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
