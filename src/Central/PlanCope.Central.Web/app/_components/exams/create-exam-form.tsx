"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { createExamSchema, type CreateExamValues } from "../../_lib/schema/exam";
import { callCentral } from "../../_lib/api/client";
import { getErrorMessage } from "../../_lib/json";
import { TextField, TextAreaField } from "../ui/text-field";
import { Button } from "../ui/button";
import { Banner } from "../ui/banner";
import type { CreateExamRequest, ExamSummary } from "../../_lib/contracts";

export function CreateExamForm() {
  const router = useRouter();
  const [serverError, setServerError] = useState<string | null>(null);
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting }
  } = useForm<CreateExamValues>({
    resolver: zodResolver(createExamSchema),
    defaultValues: { code: "", title: "", subject: "", level: "", area: "", description: "" }
  });

  async function onSubmit(values: CreateExamValues) {
    setServerError(null);
    const payload: CreateExamRequest = {
      code: values.code,
      title: values.title,
      subject: values.subject || null,
      level: values.level || null,
      area: values.area || null,
      description: values.description || null
    };

    try {
      const created = await callCentral<ExamSummary>("exams", {
        method: "POST",
        body: JSON.stringify(payload)
      });
      router.replace(`/exams/${created.id}`);
      router.refresh();
    } catch (error) {
      setServerError(getErrorMessage(error, "No se pudo crear el examen."));
    }
  }

  return (
    <form onSubmit={handleSubmit(onSubmit)} noValidate className="card">
      <div className="card__body">
        {serverError && <Banner tone="error">{serverError}</Banner>}
        <div className="cols-2" style={{ marginTop: serverError ? "var(--space-4)" : 0 }}>
          <TextField
            label="Código"
            placeholder="Ej. MAT-2026-01"
            hint="Identificador único del examen."
            required
            error={errors.code?.message}
            {...register("code")}
          />
          <TextField
            label="Materia"
            placeholder="Ej. Matemática"
            error={errors.subject?.message}
            {...register("subject")}
          />
        </div>
        <TextField
          label="Título"
          placeholder="Ej. Evaluación de Matemática — Unidad 1"
          required
          error={errors.title?.message}
          {...register("title")}
        />
        <div className="cols-2">
          <TextField
            label="Curso / grado"
            placeholder="Ej. 1° año"
            error={errors.level?.message}
            {...register("level")}
          />
          <TextField label="Área" placeholder="Ej. Ciencias exactas" error={errors.area?.message} {...register("area")} />
        </div>
        <TextAreaField
          label="Descripción"
          placeholder="Breve descripción del examen (opcional)."
          error={errors.description?.message}
          {...register("description")}
        />
        <div className="row">
          <Button type="submit" disabled={isSubmitting}>
            {isSubmitting ? "Creando…" : "Crear examen"}
          </Button>
        </div>
      </div>
    </form>
  );
}
