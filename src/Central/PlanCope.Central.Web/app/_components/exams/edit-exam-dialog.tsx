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
import { areaOptions, courseOptions } from "../../_lib/exams/catalog";
import { GradeSectionPicker } from "../shared/grade-section-picker";

interface EditExamButtonProps {
  exam: ExamSummary;
  canEditExams: boolean;
}

interface EditExamErrors {
  title?: string;
  form?: string;
}

function mapUpdateExamError(error: unknown): EditExamErrors {
  const message = getErrorMessage(error, "").toLowerCase();
  if (message.includes("title") || message.includes("título")) {
    return { title: "Revisá el título del examen." };
  }
  return { form: "No se pudieron guardar los datos. Revisá los campos e intentá de nuevo." };
}

// "Editar datos" del examen: título, cursos, área y materia.
export function EditExamButton({ exam, canEditExams }: EditExamButtonProps) {
  const router = useRouter();
  const [open, setOpen] = useState(false);
  const [title, setTitle] = useState(exam.title);
  const [courses, setCourses] = useState(exam.courses ?? []);
  const [areaChoice, setAreaChoice] = useState(() => exam.area && areaOptions.includes(exam.area as (typeof areaOptions)[number]) ? exam.area : exam.area ? "Otro" : "");
  const [customArea, setCustomArea] = useState(() => exam.area && !areaOptions.includes(exam.area as (typeof areaOptions)[number]) ? exam.area : "");
  const [subject, setSubject] = useState(exam.subject ?? "");
  const [errors, setErrors] = useState<EditExamErrors>({});
  const [pending, setPending] = useState(false);

  if (!canEditExams) return null;

  function resetForm() {
    setTitle(exam.title);
    setCourses(exam.courses ?? []);
    setAreaChoice(exam.area && areaOptions.includes(exam.area as (typeof areaOptions)[number]) ? exam.area : exam.area ? "Otro" : "");
    setCustomArea(exam.area && !areaOptions.includes(exam.area as (typeof areaOptions)[number]) ? exam.area : "");
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
    if (courses.length === 0 || (areaChoice === "Otro" && !customArea.trim())) {
      setErrors({ form: courses.length === 0 ? "Seleccioná al menos un curso." : "Ingresá el nombre del área." });
      return;
    }
    setErrors({});
    setPending(true);
    const body: UpdateExamRequest = {
      title: trimmedTitle,
      courses,
      area: areaChoice === "Otro" ? customArea.trim() : areaChoice || null,
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
        <DialogContent className="grid-rows-[minmax(0,1fr)] overflow-hidden sm:max-w-2xl">
          <form onSubmit={onSubmit} noValidate className="grid min-h-0 max-h-full grid-rows-[auto_minmax(0,1fr)_auto] gap-4">
            <DialogHeader>
              <DialogTitle>Editar datos</DialogTitle>
              <DialogDescription>Datos generales del examen.</DialogDescription>
            </DialogHeader>

            <div className="min-h-0 overflow-y-auto overscroll-contain">
              <FieldGroup className="gap-4">
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

              <Field>
                <GradeSectionPicker
                  mode="multi"
                  grades={courseOptions.map(course => ({ value: course.key, label: course.label, group: course.level }))}
                  value={courses}
                  onValueChange={next => setCourses(Array.isArray(next) ? next : [next])}
                  showSection={false}
                />
              </Field>
              <div className="grid gap-4 sm:grid-cols-2">
                <Field>
                  <FieldLabel htmlFor="edit-exam-area">Área</FieldLabel>
                  <select id="edit-exam-area" className="h-9 rounded-md border bg-background px-3 text-sm"
                    value={areaChoice}
                    onChange={event => setAreaChoice(event.target.value)}>
                    <option value="">Seleccionar área</option>
                    {areaOptions.map(value => <option key={value} value={value}>{value}</option>)}
                  </select>
                </Field>
                {areaChoice === "Otro" &&
                  <Field data-invalid={!customArea.trim() ? true : undefined}>
                    <FieldLabel htmlFor="edit-exam-custom-area">Área</FieldLabel>
                    <Input id="edit-exam-custom-area" value={customArea} onChange={event => setCustomArea(event.target.value)} />
                    {!customArea.trim() && <FieldError>Ingresá el nombre del área.</FieldError>}
                  </Field>}
                <Field>
                  <FieldLabel htmlFor="edit-exam-subject">Materia</FieldLabel>
                  <Input id="edit-exam-subject" value={subject} onChange={event => setSubject(event.target.value)} />
                </Field>
              </div>
              </FieldGroup>

              {errors.form && (
                <p role="alert" className="mt-4 text-sm text-destructive">
                  {errors.form}
                </p>
              )}
            </div>

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
