"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { callCentral } from "../../_lib/api/client";
import { publishErrorMessage } from "../../_lib/exams/publish-errors";
import { publishSupersedeMessage } from "../../_lib/exams/version-state";
import {
  type ExamDocument
} from "../../_lib/schema/exam";
import type { PublishExamVersionResponse } from "../../_lib/contracts";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle
} from "@/components/ui/dialog";
import { Field, FieldError, FieldLabel } from "@/components/ui/field";
import { Input } from "@/components/ui/input";

interface PublishDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  examId: string;
  versionId: string;
  versionNumber: number;
  currentPublishedVersionNumber?: number | null;
  document: ExamDocument;
  /** Persists unsaved changes first; returns false when saving failed. */
  onSaveBeforePublish: () => Promise<boolean>;
  /** Called after a successful publish so the builder can refresh into read-only mode. */
  onPublished: () => void;
}

export function PublishDialog({
  open,
  onOpenChange,
  examId,
  versionId,
  versionNumber,
  currentPublishedVersionNumber = null,
  document,
  onSaveBeforePublish,
  onPublished
}: PublishDialogProps) {
  const router = useRouter();
  const [division, setDivision] = useState("");
  const [subject, setSubject] = useState("");
  const [publishError, setPublishError] = useState<string | null>(null);
  const [publishing, setPublishing] = useState(false);
  const [done, setDone] = useState(false);

  const questionCount = document.questions.length;
  const supersedeMessage = publishSupersedeMessage({
    currentPublishedNumber: currentPublishedVersionNumber,
    versionNumber
  });

  async function handlePublish() {
    setPublishError(null);
    setPublishing(true);
    try {
      const saved = await onSaveBeforePublish();
      if (!saved) {
        setPublishError("No se pudo guardar el examen; no se publicó.");
        return;
      }
      const payload = {
        subject: subject.trim() || null,
        division: division.trim() || null
      };
      await callCentral<PublishExamVersionResponse>(
        `exams/versions/${encodeURIComponent(versionId)}/publish`,
        { method: "POST", body: JSON.stringify(payload) }
      );
      setDone(true);
      onPublished();
    } catch (error) {
      setPublishError(publishErrorMessage(error));
    } finally {
      setPublishing(false);
    }
  }

  function handleOpenChange(nextOpen: boolean) {
    if (nextOpen) {
      setSubject(document.subject ?? "");
      setDivision("");
        setPublishError(null);
      setDone(false);
    }
    onOpenChange(nextOpen);
  }

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogContent className="sm:max-w-lg">
        {done ? (
          <>
            <DialogHeader>
              <DialogTitle>Versión publicada</DialogTitle>
            </DialogHeader>
            <p className="text-sm text-muted-foreground">
              Publicado. Los equipos la reciben en la próxima sincronización (unos 30 s) o al buscar exámenes nuevos.
            </p>
            <DialogFooter>
              <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
                Cerrar
              </Button>
              <Button type="button" onClick={() => router.push(`/exams/${examId}`)}>
                Ir al examen
              </Button>
            </DialogFooter>
          </>
        ) : (
          <>
            <DialogHeader>
              <DialogTitle>Publicar versión</DialogTitle>
              <DialogDescription>La versión se entrega a todos los equipos.</DialogDescription>
            </DialogHeader>

            <div className="grid gap-5">
              <div className="grid gap-4 sm:grid-cols-2">
                <Field>
                  <FieldLabel htmlFor="publish-subject">Materia (opcional)</FieldLabel>
                  <Input id="publish-subject" value={subject} onChange={event => setSubject(event.target.value)} />
                </Field>
                <Field>
                  <FieldLabel htmlFor="publish-division">División (opcional)</FieldLabel>
                  <Input id="publish-division" value={division} onChange={event => setDivision(event.target.value)} />
                </Field>
              </div>

              <div className="rounded-lg border bg-muted/40 p-3 text-sm">
                <p className="font-medium">Se entrega a todos los equipos</p>
                {supersedeMessage && <p className="text-muted-foreground">{supersedeMessage}</p>}
                <p className="text-muted-foreground">
                  {questionCount} {questionCount === 1 ? "pregunta" : "preguntas"}
                </p>
              </div>

              {publishError && <FieldError>{publishError}</FieldError>}
            </div>

            <DialogFooter>
              <Button type="button" variant="outline" disabled={publishing} onClick={() => onOpenChange(false)}>
                Cancelar
              </Button>
              <Button type="button" disabled={publishing} onClick={() => void handlePublish()}>
                {publishing ? "Publicando…" : "Publicar"}
              </Button>
            </DialogFooter>
          </>
        )}
      </DialogContent>
    </Dialog>
  );
}
