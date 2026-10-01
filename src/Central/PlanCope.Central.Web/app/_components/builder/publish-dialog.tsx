"use client";

import { useEffect, useMemo, useState } from "react";
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
import { GradeSectionPicker, type GradeSectionOption } from "../shared/grade-section-picker";
import { courseOptions } from "../../_lib/exams/catalog";

interface PublicationSectionOption {
  gradeValue: string;
  value: string;
  label: string;
  shift?: string | null;
}

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
  const [grade, setGrade] = useState("");
  const [division, setDivision] = useState("");
  const [sections, setSections] = useState<PublicationSectionOption[]>([]);
  const [sectionsError, setSectionsError] = useState<string | null>(null);
  const [loadedGradeQuery, setLoadedGradeQuery] = useState<string | null>(null);
  const [subject, setSubject] = useState("");
  const [publishError, setPublishError] = useState<string | null>(null);
  const [publishing, setPublishing] = useState(false);
  const [done, setDone] = useState(false);

  const questionCount = document.questions.length;
  const gradeKeys = document.courses ?? [];
  const gradeQuery = [...gradeKeys].sort().join(",");
  const sectionsLoading = open && gradeQuery.length > 0 && loadedGradeQuery !== gradeQuery;
  const pickerGrades = useMemo<GradeSectionOption[]>(() => {
    const selectedGradeKeys = new Set(gradeQuery.split(",").filter(Boolean));
    return courseOptions
      .filter(course => selectedGradeKeys.has(course.key))
      .map(course => ({
        value: course.key,
        label: course.label,
        group: course.level,
        sections: sections.filter(section => section.gradeValue === course.key).map(section => ({
          value: section.value,
          label: section.label,
          shift: section.shift
        }))
      }));
  }, [gradeQuery, sections]);

  useEffect(() => {
    if (!open || gradeQuery.length === 0) return;
    let active = true;
    const params = new URLSearchParams();
    gradeQuery.split(",").forEach(gradeKey => params.append("grades", gradeKey));
    void callCentral<PublicationSectionOption[]>(`rosters/publication-sections?${params.toString()}`)
      .then(options => {
        if (active) {
          setSections(options);
          if (options.length === 0) {
            setSectionsError("No hay divisiones disponibles para los grados seleccionados.");
          }
          setLoadedGradeQuery(gradeQuery);
        }
      })
      .catch(() => {
        if (active) {
          setSectionsError("No se pudieron cargar las divisiones disponibles.");
          setLoadedGradeQuery(gradeQuery);
        }
      });
    return () => { active = false; };
  }, [open, gradeQuery]);
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
        division: division.trim() && division !== "all" ? division.trim() : null
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
      setGrade(gradeKeys.length === 1 ? gradeKeys[0] : "");
      setDivision("");
      setSections([]);
      setSectionsError(null);
      setLoadedGradeQuery(null);
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
                {sectionsError ? (
                  <Field>
                    <FieldLabel htmlFor="publish-division">División (opcional)</FieldLabel>
                    <Input id="publish-division" value={division} onChange={event => setDivision(event.target.value)} />
                    <p className="text-xs text-muted-foreground">{sectionsError}</p>
                  </Field>
                ) : (
                  <div className="grid gap-2 sm:col-span-2">
                    <GradeSectionPicker
                      grades={pickerGrades}
                      value={grade}
                      onValueChange={next => setGrade(typeof next === "string" ? next : "")}
                      sectionValue={division}
                      onSectionValueChange={setDivision}
                      includeAllSections
                      forceSection
                      disabled={sectionsLoading}
                    />
                    {sectionsLoading && <p className="text-xs text-muted-foreground">Cargando divisiones…</p>}
                  </div>
                )}
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
