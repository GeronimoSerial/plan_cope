"use client";

import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { publishSchema, type PublishValues } from "../../_lib/schema/exam";
import { callCentral } from "../../_lib/api/client";
import { getErrorMessage } from "../../_lib/json";
import { TextField } from "../ui/text-field";
import { Button } from "../ui/button";
import { Banner } from "../ui/banner";
import { ConfirmDialog } from "../ui/confirm-dialog";
import type { PublishExamVersionRequest, PublishExamVersionResponse } from "../../_lib/contracts";

interface PublishPanelProps {
  versionId: string;
  defaultSubject: string | null;
  hasUnsavedChanges: boolean;
  onPublished: () => void;
}

export function PublishPanel({ versionId, defaultSubject, hasUnsavedChanges, onPublished }: PublishPanelProps) {
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState<PublishValues | null>(null);

  const {
    register,
    handleSubmit,
    formState: { errors }
  } = useForm<PublishValues>({
    resolver: zodResolver(publishSchema),
    defaultValues: { subject: defaultSubject ?? "", grade: "", division: "" }
  });

  function onSubmit(values: PublishValues) {
    setError(null);
    setPending(values);
    setConfirmOpen(true);
  }

  async function confirmPublish() {
    if (!pending) {
      return;
    }
    setBusy(true);
    setError(null);
    const payload: PublishExamVersionRequest = {
      subject: pending.subject || null,
      grade: pending.grade,
      division: pending.division || null
    };
    try {
      await callCentral<PublishExamVersionResponse>(`exams/versions/${encodeURIComponent(versionId)}/publish`, {
        method: "POST",
        body: JSON.stringify(payload)
      });
      setConfirmOpen(false);
      onPublished();
    } catch (caught) {
      setConfirmOpen(false);
      setError(getErrorMessage(caught, "No se pudo publicar la versión."));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="card">
      <div className="card__header">
        <h2>Publicar versión</h2>
      </div>
      <form className="card__body stack" onSubmit={handleSubmit(onSubmit)} noValidate>
        <p style={{ color: "var(--text-muted)" }}>
          Al publicar, la versión queda inmutable y disponible para sincronizar con las escuelas.
        </p>
        {hasUnsavedChanges && <Banner tone="info">Guardá los cambios antes de publicar.</Banner>}
        {error && <Banner tone="error">{error}</Banner>}

        <div className="cols-2">
          <TextField label="Materia" error={errors.subject?.message} {...register("subject")} />
          <TextField label="Curso / grado" required error={errors.grade?.message} {...register("grade")} />
        </div>
        <TextField label="División (opcional)" error={errors.division?.message} {...register("division")} />

        <div className="row">
          <Button type="submit" disabled={hasUnsavedChanges}>
            Publicar versión
          </Button>
        </div>
      </form>

      <ConfirmDialog
        open={confirmOpen}
        title="Publicar esta versión"
        description="Una vez publicada no podrás editar las preguntas de esta versión. ¿Querés continuar?"
        confirmLabel="Sí, publicar"
        busy={busy}
        onConfirm={() => void confirmPublish()}
        onCancel={() => setConfirmOpen(false)}
      />
    </div>
  );
}
