"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { arrayMove } from "@dnd-kit/sortable";
import { CircleCheckIcon, CircleDotIcon, Loader2Icon } from "lucide-react";
import { toast } from "sonner";
import { callCentral } from "../../_lib/api/client";
import { getErrorMessage } from "../../_lib/json";
import {
  examDocumentSchema,
  type ExamDocument,
  type Question,
  type QuestionType
} from "../../_lib/schema/exam";
import {
  blankQuestion,
  cloneQuestion,
  documentToReplaceRequest,
  evaluateDocumentReadiness,
  mergeDocumentReadiness,
  type DocumentReadiness,
  type ServerReadiness
} from "../../_lib/schema/mappers";
import { QuestionList } from "./question-list";
import { ExamPreview } from "./exam-preview";
import { PublishDialog } from "./publish-dialog";
import { CreateVersionDialog } from "../exams/create-version-dialog";
import { useNavigationGuard } from "../layout/navigation-guard";
import { versionStatusLine, versionStatusTerm } from "../../_lib/exams/version-state";
import { Alert, AlertDescription } from "@/components/ui/alert";
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator
} from "@/components/ui/breadcrumb";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Field, FieldError, FieldLabel } from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Textarea } from "@/components/ui/textarea";
import { TermLabel } from "../help/term-hint";
import type { ExamVersion, PublishBlockedReason } from "../../_lib/contracts";

interface ExamBuilderProps {
  examId: string;
  examCode: string;
  examTitle: string;
  versionId: string;
  versionNumber: number;
  status: string;
  isCurrent: boolean;
  basedOnVersionNumber?: number | null;
  currentPublishedVersionNumber?: number | null;
  nextVersionNumber: number;
  initialDocument: ExamDocument;
  canPublish: boolean;
  publishBlockedReason?: PublishBlockedReason | null;
  autoOpenPublish?: boolean;
  canEditExams: boolean;
}

function blockedReasonMessage(reason: DocumentReadiness["blockedReason"]): string {
  if (reason === "no_blocks") {
    return "Agregá al menos una pregunta.";
  }
  // Unknown/unmapped server reasons keep the button disabled and fall back to this generic text.
  return "No se puede publicar todavía.";
}

export function ExamBuilder({
  examId,
  examCode,
  examTitle,
  versionId,
  versionNumber,
  status,
  isCurrent,
  basedOnVersionNumber = null,
  currentPublishedVersionNumber = null,
  nextVersionNumber,
  initialDocument,
  canPublish,
  publishBlockedReason,
  autoOpenPublish = false,
  canEditExams
}: ExamBuilderProps) {
  const router = useRouter();
  const { setDirty, intercept } = useNavigationGuard();
  const [document, setDocument] = useState<ExamDocument>(initialDocument);
  const [savedSnapshot, setSavedSnapshot] = useState(() => JSON.stringify(initialDocument));
  const [published, setPublished] = useState(() => status.toLowerCase() === "published");
  const [activeTab, setActiveTab] = useState("edit");
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [saving, setSaving] = useState(false);
  const [publishOpen, setPublishOpen] = useState(() => autoOpenPublish && status.toLowerCase() !== "published");
  const [serverReadiness, setServerReadiness] = useState<ServerReadiness>({
    canPublish,
    blockedReason: publishBlockedReason ?? null
  });
  const [createVersionOpen, setCreateVersionOpen] = useState(false);

  const dirty = useMemo(() => JSON.stringify(document) !== savedSnapshot, [document, savedSnapshot]);
  const isReadOnly = published || !canEditExams;
  const statusLine = versionStatusLine({ versionNumber, status, isCurrent, basedOnVersionNumber });
  const statusTerm = versionStatusTerm({ status, isCurrent });
  const readiness = useMemo(
    () => mergeDocumentReadiness(evaluateDocumentReadiness(document), serverReadiness, dirty),
    [document, dirty, serverReadiness]
  );

  const documentRef = useRef(document);
  useEffect(() => {
    documentRef.current = document;
  }, [document]);

  const save = useCallback(async (): Promise<boolean> => {
    const current = documentRef.current;
    const result = examDocumentSchema.safeParse(current);
    if (!result.success) {
      const map: Record<string, string> = {};
      for (const issue of result.error.issues) {
        const key = issue.path.join(".");
        if (!map[key]) {
          map[key] = issue.message;
        }
      }
      setErrors(map);
      toast.error("Revisá los campos marcados antes de guardar.");
      return false;
    }

    setErrors({});
    setSaving(true);
    try {
      const effective: ExamDocument = result.data;
      const updated = await callCentral<ExamVersion>(`exams/versions/${encodeURIComponent(versionId)}/document`, {
        method: "PUT",
        body: JSON.stringify(documentToReplaceRequest(effective))
      });
      // La respuesta trae la readiness recalculada: sin esto el boton Publicar se quedaba
      // deshabilitado con el canPublish viejo apenas dirty volvia a false.
      const local = evaluateDocumentReadiness(effective);
      setServerReadiness({
        canPublish: typeof updated?.canPublish === "boolean" ? updated.canPublish : local.canPublish,
        blockedReason:
          updated?.publishBlockedReason ??
          (local.blockedReason === "unknown" ? null : local.blockedReason)
      });
      setSavedSnapshot(JSON.stringify(current));
      toast.success("Cambios guardados.");
      return true;
    } catch (error) {
      toast.error(getErrorMessage(error, "No se pudo guardar el examen."));
      return false;
    } finally {
      setSaving(false);
    }
  }, [versionId]);

  // Ctrl/Cmd+S guarda sin salir del builder.
  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === "s") {
        event.preventDefault();
        if (!isReadOnly && !saving) {
          void save();
        }
      }
    }
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [isReadOnly, saving, save]);

  // Evita perder cambios al cerrar/recargar la pestaña.
  useEffect(() => {
    if (!dirty || isReadOnly) {
      return;
    }
    function onBeforeUnload(event: BeforeUnloadEvent) {
      event.preventDefault();
      event.returnValue = "";
    }
    window.addEventListener("beforeunload", onBeforeUnload);
    return () => window.removeEventListener("beforeunload", onBeforeUnload);
  }, [dirty, isReadOnly]);

  // Registra el estado sucio en el guard compartido: sidebar, header y breadcrumbs lo consultan.
  useEffect(() => {
    setDirty(dirty, isReadOnly);
    return () => setDirty(false, false);
  }, [dirty, isReadOnly, setDirty]);

  const patchDocument = useCallback((patch: Partial<ExamDocument>) => {
    setDocument(current => ({ ...current, ...patch }));
  }, []);

  const addQuestion = useCallback((type: QuestionType) => {
    setDocument(current => ({ ...current, questions: [...current.questions, blankQuestion(type)] }));
    setActiveTab("edit");
  }, []);

  const updateQuestion = useCallback((id: string, next: Question) => {
    setDocument(current => ({
      ...current,
      questions: current.questions.map(question => (question.id === id ? next : question))
    }));
  }, []);

  const removeQuestion = useCallback((id: string) => {
    setDocument(current => ({ ...current, questions: current.questions.filter(question => question.id !== id) }));
  }, []);

  const duplicateQuestion = useCallback((id: string) => {
    setDocument(current => {
      const index = current.questions.findIndex(question => question.id === id);
      if (index < 0) {
        return current;
      }
      const next = [...current.questions];
      next.splice(index + 1, 0, cloneQuestion(current.questions[index]));
      return { ...current, questions: next };
    });
  }, []);

  const reorderQuestion = useCallback((activeId: string, overId: string) => {
    setDocument(current => {
      const from = current.questions.findIndex(question => question.id === activeId);
      const to = current.questions.findIndex(question => question.id === overId);
      return from >= 0 && to >= 0 ? { ...current, questions: arrayMove(current.questions, from, to) } : current;
    });
  }, []);

  function requestNavigation(href: string) {
    if (!intercept(() => router.push(href))) {
      router.push(href);
    }
  }

  const publishBlockedId = "publish-blocked-reason";
  const publishBlockedMessage = blockedReasonMessage(readiness.blockedReason);

  return (
    <div className="grid gap-4">
      <header className="grid gap-3">
        <Breadcrumb>
          <BreadcrumbList>
            <BreadcrumbItem>
              <BreadcrumbLink render={<button type="button" />} onClick={() => requestNavigation("/exams")}>
                Exámenes
              </BreadcrumbLink>
            </BreadcrumbItem>
            <BreadcrumbSeparator />
            <BreadcrumbItem>
              <BreadcrumbLink
                render={<button type="button" />}
                onClick={() => requestNavigation(`/exams/${examId}`)}
              >
                {examCode}
              </BreadcrumbLink>
            </BreadcrumbItem>
            <BreadcrumbSeparator />
            <BreadcrumbItem>
              <BreadcrumbPage>v{versionNumber}</BreadcrumbPage>
            </BreadcrumbItem>
          </BreadcrumbList>
        </Breadcrumb>

        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="flex min-w-0 flex-wrap items-center gap-3">
            <h1 className="truncate text-xl font-semibold tracking-tight">{document.title || examTitle}</h1>
            <SaveState dirty={dirty} saving={saving} readOnly={isReadOnly} />
          </div>
          {!isReadOnly && (
            <div className="flex flex-wrap items-start gap-2">
              <Button type="button" variant="outline" disabled={saving || !dirty} onClick={() => void save()}>
                {saving ? "Guardando…" : "Guardar"}
              </Button>
              <div className="grid gap-1">
                <Button
                  type="button"
                  disabled={!readiness.canPublish}
                  aria-describedby={!readiness.canPublish ? publishBlockedId : undefined}
                  onClick={() => setPublishOpen(true)}
                >
                  Publicar
                </Button>
                {!readiness.canPublish && (
                  <p id={publishBlockedId} className="max-w-56 text-xs text-muted-foreground">
                    {publishBlockedMessage}
                  </p>
                )}
              </div>
            </div>
          )}
        </div>

        <p className="text-sm text-muted-foreground">
          <TermLabel term={statusTerm}>{statusLine}</TermLabel>
        </p>

        <p className="text-sm text-muted-foreground">
          Armá las preguntas y definí la regla de puntaje de cada pregunta de opción múltiple. Al guardar y publicar, los nodos lo reciben en la próxima
          sincronización.
        </p>
      </header>

      {published && (
        <Alert>
          <AlertDescription className="flex flex-wrap items-center justify-between gap-3">
            <span>Esta versión ya está publicada y no se puede editar.</span>
            {canEditExams && <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={() => setCreateVersionOpen(true)}
            >
              Crear versión a partir de esta
            </Button>}
          </AlertDescription>
        </Alert>
      )}

      <Tabs value={activeTab} onValueChange={value => setActiveTab(String(value))}>
        <TabsList>
          <TabsTrigger value="edit">Edición</TabsTrigger>
          <TabsTrigger value="preview">Vista previa</TabsTrigger>
        </TabsList>

        <TabsContent value="edit" className="grid gap-4">
          <Card>
            <CardHeader>
              <CardTitle>Datos del examen</CardTitle>
            </CardHeader>
            <CardContent className="grid gap-4">
              <div className="grid gap-4 sm:grid-cols-2">
                <Field>
                  <FieldLabel htmlFor="meta-code">Código</FieldLabel>
                  <Input id="meta-code" value={document.code} readOnly disabled />
                </Field>
                <Field data-invalid={errors.title ? true : undefined}>
                  <FieldLabel htmlFor="meta-title">Título</FieldLabel>
                  <Input
                    id="meta-title"
                    value={document.title}
                    disabled={isReadOnly}
                    aria-invalid={errors.title ? true : undefined}
                    onChange={event => patchDocument({ title: event.target.value })}
                  />
                  {errors.title && <FieldError>{errors.title}</FieldError>}
                </Field>
              </div>
              <div className="grid gap-4 sm:grid-cols-3">
                <Field>
                  <FieldLabel htmlFor="meta-subject">Materia</FieldLabel>
                  <Input
                    id="meta-subject"
                    value={document.subject ?? ""}
                    disabled={isReadOnly}
                    onChange={event => patchDocument({ subject: event.target.value || undefined })}
                  />
                </Field>
                <Field>
                  <FieldLabel htmlFor="meta-level">
                    <TermLabel term="curso-grado">Curso / grado</TermLabel>
                  </FieldLabel>
                  <Input
                    id="meta-level"
                    value={document.level ?? ""}
                    disabled={isReadOnly}
                    onChange={event => patchDocument({ level: event.target.value || undefined })}
                  />
                </Field>
                <Field>
                  <FieldLabel htmlFor="meta-area">Área</FieldLabel>
                  <Input
                    id="meta-area"
                    value={document.area ?? ""}
                    disabled={isReadOnly}
                    onChange={event => patchDocument({ area: event.target.value || undefined })}
                  />
                </Field>
              </div>
              <Field>
                <FieldLabel htmlFor="meta-description">Descripción</FieldLabel>
                <Textarea
                  id="meta-description"
                  value={document.description ?? ""}
                  disabled={isReadOnly}
                  onChange={event => patchDocument({ description: event.target.value || undefined })}
                />
              </Field>
            </CardContent>
          </Card>

          <QuestionList
            questions={document.questions}
            errors={errors}
            disabled={isReadOnly}
            onReorder={reorderQuestion}
            onUpdate={updateQuestion}
            onRemove={removeQuestion}
            onDuplicate={duplicateQuestion}
            onAdd={addQuestion}
          />
          {errors.questions && <FieldError>{errors.questions}</FieldError>}
        </TabsContent>

        <TabsContent value="preview">
          <ExamPreview document={document} />
        </TabsContent>
      </Tabs>

      {canEditExams && <PublishDialog
        open={publishOpen}
        onOpenChange={setPublishOpen}
        examId={examId}
        versionId={versionId}
        versionNumber={versionNumber}
        currentPublishedVersionNumber={currentPublishedVersionNumber}
        document={document}
        onSaveBeforePublish={async () => (dirty ? save() : true)}
        onPublished={() => {
          setPublished(true);
          router.refresh();
        }}
      />}

      {canEditExams && <CreateVersionDialog
        examId={examId}
        open={createVersionOpen}
        onOpenChange={setCreateVersionOpen}
        sourceVersionId={versionId}
        sourceNumber={versionNumber}
        sourcePublished
        nextNumber={nextVersionNumber}
      />}
    </div>
  );
}

function SaveState({ dirty, saving, readOnly }: { dirty: boolean; saving: boolean; readOnly: boolean }) {
  if (readOnly) {
    return null;
  }
  if (saving) {
    return (
      <span className="inline-flex items-center gap-1.5 text-xs text-muted-foreground">
        <Loader2Icon className="size-3.5 animate-spin" />
        Guardando…
      </span>
    );
  }
  if (dirty) {
    return (
      <span className="inline-flex items-center gap-1.5 text-xs text-amber-600 dark:text-amber-500">
        <CircleDotIcon className="size-3.5" />
        Cambios sin guardar
      </span>
    );
  }
  return (
    <span className="inline-flex items-center gap-1.5 text-xs text-muted-foreground">
      <CircleCheckIcon className="size-3.5" />
      Guardado
    </span>
  );
}
