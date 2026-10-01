"use client";

import { useEffect, useMemo, useState, type ReactNode } from "react";
import { useRouter } from "next/navigation";
import { XIcon } from "lucide-react";
import { callCentral } from "../../_lib/api/client";
import {
  buildPublishRequest,
  searchNodes,
  searchSchools,
  summarizeTargets,
  validatePublishTargets,
  type NodeTargetRow,
  type PublishTargetMode,
  type PublishTargetValidationError,
  type SchoolTargetRow
} from "../../_lib/exams/publish-targets";
import { publishErrorMessage } from "../../_lib/exams/publish-errors";
import { publishSupersedeMessage } from "../../_lib/exams/version-state";
import type { ExamDocument } from "../../_lib/schema/exam";
import { TermLabel } from "../help/term-hint";
import type { PublishExamVersionResponse } from "../../_lib/contracts";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
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
import { Label } from "@/components/ui/label";
import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group";
import { Skeleton } from "@/components/ui/skeleton";

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

const MODES: Array<{ value: PublishTargetMode; label: string }> = [
  { value: "all", label: "Todas las escuelas" },
  { value: "schools", label: "Escuelas específicas" },
  { value: "nodes", label: "Nodos específicos" }
];

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
  const [subject, setSubject] = useState("");
  const [mode, setMode] = useState<PublishTargetMode>("all");
  const [schools, setSchools] = useState<SchoolTargetRow[] | null>(null);
  const [nodes, setNodes] = useState<NodeTargetRow[] | null>(null);
  const [loadingTargets, setLoadingTargets] = useState(false);
  const [targetsError, setTargetsError] = useState<string | null>(null);
  const [schoolQuery, setSchoolQuery] = useState("");
  const [nodeQuery, setNodeQuery] = useState("");
  const [selectedSchools, setSelectedSchools] = useState<SchoolTargetRow[]>([]);
  const [selectedNodes, setSelectedNodes] = useState<NodeTargetRow[]>([]);
  const [validationError, setValidationError] = useState<PublishTargetValidationError | null>(null);
  const [publishError, setPublishError] = useState<string | null>(null);
  const [publishing, setPublishing] = useState(false);
  const [done, setDone] = useState(false);

  useEffect(() => {
    if (!open) {
      return;
    }
    setGrade(document.level ?? "");
    setSubject(document.subject ?? "");
    setMode("all");
    setSchoolQuery("");
    setNodeQuery("");
    setSelectedSchools([]);
    setSelectedNodes([]);
    setValidationError(null);
    setPublishError(null);
    setDone(false);
  }, [open, document.level, document.subject]);

  useEffect(() => {
    if (!open) {
      return;
    }
    const shouldLoad = (mode === "schools" && schools === null) || (mode === "nodes" && nodes === null);
    if (!shouldLoad) {
      return;
    }
    let cancelled = false;
    setLoadingTargets(true);
    setTargetsError(null);
    const path = mode === "schools" ? "admin/schools" : "admin/activation/nodes";
    callCentral<unknown[]>(path)
      .then(rows => {
        if (cancelled) {
          return;
        }
        if (mode === "schools") {
          setSchools(rows as SchoolTargetRow[]);
        } else {
          setNodes(rows as NodeTargetRow[]);
        }
      })
      .catch(() => {
        if (!cancelled) {
          setTargetsError("No se pudieron cargar las opciones. Probá de nuevo.");
        }
      })
      .finally(() => {
        if (!cancelled) {
          setLoadingTargets(false);
        }
      });
    return () => {
      cancelled = true;
    };
  }, [open, mode, schools, nodes]);

  const schoolResults = useMemo(() => searchSchools(schools ?? [], schoolQuery), [schools, schoolQuery]);
  const nodeResults = useMemo(() => searchNodes(nodes ?? [], nodeQuery), [nodes, nodeQuery]);

  const questionCount = document.questions.length;
  const targetSummary = summarizeTargets(mode, selectedSchools, selectedNodes);
  const supersedeMessage = publishSupersedeMessage({
    currentPublishedNumber: currentPublishedVersionNumber,
    versionNumber
  });

  function toggleSchool(row: SchoolTargetRow) {
    setSelectedSchools(current =>
      current.some(item => item.id === row.id) ? current.filter(item => item.id !== row.id) : [...current, row]
    );
  }

  function toggleNode(row: NodeTargetRow) {
    setSelectedNodes(current =>
      current.some(item => item.id === row.id) ? current.filter(item => item.id !== row.id) : [...current, row]
    );
  }

  async function handlePublish() {
    const validation = validatePublishTargets({
      grade,
      mode,
      schoolIds: selectedSchools.map(school => school.cue),
      nodeIds: selectedNodes.map(node => node.id)
    });
    if (validation) {
      setValidationError(validation);
      return;
    }

    setValidationError(null);
    setPublishError(null);
    setPublishing(true);
    try {
      const saved = await onSaveBeforePublish();
      if (!saved) {
        setPublishError("No se pudo guardar el examen; no se publicó.");
        return;
      }
      const payload = buildPublishRequest({
        grade,
        subject,
        mode,
        schoolIds: selectedSchools.map(school => school.cue),
        nodeIds: selectedNodes.map(node => node.id)
      });
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

  return (
    <Dialog
      open={open}
      onOpenChange={next => {
        onOpenChange(next);
      }}
    >
      <DialogContent className="sm:max-w-lg">
        {done ? (
          <>
            <DialogHeader>
              <DialogTitle>Versión publicada</DialogTitle>
            </DialogHeader>
            <p className="text-sm text-muted-foreground">
              Publicado. Los nodos lo reciben en la próxima sincronización (unos 30 s) o con &apos;Buscar exámenes
              nuevos&apos; en la app instalada.
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
              <DialogDescription>Elegí a quién se entrega la versión.</DialogDescription>
            </DialogHeader>

            <div className="grid gap-5">
              <div className="grid gap-4 sm:grid-cols-2">
                <Field data-invalid={validationError?.field === "grade" ? true : undefined}>
                  <FieldLabel htmlFor="publish-grade">
                    <TermLabel term="curso-grado">Curso / grado</TermLabel>
                  </FieldLabel>
                  <Input
                    id="publish-grade"
                    value={grade}
                    onChange={event => setGrade(event.target.value)}
                    placeholder="Ej. 6"
                  />
                </Field>
                <Field>
                  <FieldLabel htmlFor="publish-subject">Materia (opcional)</FieldLabel>
                  <Input id="publish-subject" value={subject} onChange={event => setSubject(event.target.value)} />
                </Field>
              </div>

              <Field data-invalid={validationError?.field === "targets" ? true : undefined}>
                <FieldLabel>Entrega</FieldLabel>
                <RadioGroup value={mode} onValueChange={value => setMode(value as PublishTargetMode)} className="gap-2">
                  {MODES.map(option => (
                    <div key={option.value} className="flex items-center gap-2">
                      <RadioGroupItem id={`publish-mode-${option.value}`} value={option.value} />
                      {option.value === "nodes" ? (
                        <TermLabel term="nodo">
                          <Label htmlFor={`publish-mode-${option.value}`} className="font-normal">
                            {option.label}
                          </Label>
                        </TermLabel>
                      ) : (
                        <Label htmlFor={`publish-mode-${option.value}`} className="font-normal">
                          {option.label}
                        </Label>
                      )}
                    </div>
                  ))}
                </RadioGroup>
              </Field>

              {mode === "schools" && (
                <TargetPicker
                  idPrefix="schools"
                  query={schoolQuery}
                  onQueryChange={setSchoolQuery}
                  loading={loadingTargets && schools === null}
                  error={targetsError}
                  emptyLabel="No hay escuelas que coincidan."
                  placeholder="Buscar por CUE o nombre"
                  searchLabel="Buscar escuela"
                  truncated={schoolResults.truncated}
                  shown={schoolResults.shown}
                >
                  {schoolResults.rows.map(row => (
                    <TargetRow
                      key={row.id}
                      id={`school-${row.id}`}
                      checked={selectedSchools.some(item => item.id === row.id)}
                      onToggle={() => toggleSchool(row)}
                      primary={row.name}
                      secondary={`CUE ${row.cue}`}
                    />
                  ))}
                </TargetPicker>
              )}

              {mode === "nodes" && (
                <TargetPicker
                  idPrefix="nodes"
                  query={nodeQuery}
                  onQueryChange={setNodeQuery}
                  loading={loadingTargets && nodes === null}
                  error={targetsError}
                  emptyLabel="No hay nodos que coincidan."
                  placeholder="Buscar por código, CUE o escuela"
                  searchLabel="Buscar nodo"
                  truncated={nodeResults.truncated}
                  shown={nodeResults.shown}
                >
                  {nodeResults.rows.map(row => (
                    <TargetRow
                      key={row.id}
                      id={`node-${row.id}`}
                      checked={selectedNodes.some(item => item.id === row.id)}
                      onToggle={() => toggleNode(row)}
                      primary={row.nodeCode}
                      secondary={[row.schoolName, row.cue, row.deviceName].filter(Boolean).join(" · ")}
                    />
                  ))}
                </TargetPicker>
              )}

              {(selectedSchools.length > 0 || selectedNodes.length > 0) && (
                <div className="flex flex-wrap gap-2">
                  {selectedSchools.map(row => (
                    <SelectedChip key={row.id} label={`CUE ${row.cue}`} onRemove={() => toggleSchool(row)} />
                  ))}
                  {selectedNodes.map(row => (
                    <SelectedChip key={row.id} label={row.nodeCode} onRemove={() => toggleNode(row)} />
                  ))}
                </div>
              )}

              <div className="rounded-lg border bg-muted/40 p-3 text-sm">
                <p className="font-medium">{targetSummary}</p>
                {supersedeMessage && <p className="text-muted-foreground">{supersedeMessage}</p>}
                <p className="text-muted-foreground">
                  {questionCount} {questionCount === 1 ? "pregunta" : "preguntas"}
                </p>
              </div>

              {validationError && <FieldError>{validationError.message}</FieldError>}
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

function TargetPicker({
  idPrefix,
  query,
  onQueryChange,
  loading,
  error,
  emptyLabel,
  placeholder,
  searchLabel,
  truncated,
  shown,
  children
}: {
  idPrefix: string;
  query: string;
  onQueryChange: (value: string) => void;
  loading: boolean;
  error: string | null;
  emptyLabel: string;
  placeholder: string;
  searchLabel: string;
  truncated: boolean;
  shown: number;
  children: ReactNode;
}) {
  const hasChildren = Array.isArray(children) ? children.length > 0 : Boolean(children);
  return (
    <div className="grid gap-2">
      <Input
        id={`${idPrefix}-search`}
        value={query}
        onChange={event => onQueryChange(event.target.value)}
        placeholder={placeholder}
        aria-label={searchLabel}
      />
      <div className="max-h-60 overflow-y-auto rounded-lg border">
        {loading ? (
          <div className="grid gap-2 p-3">
            <Skeleton className="h-6 w-full" />
            <Skeleton className="h-6 w-full" />
            <Skeleton className="h-6 w-2/3" />
          </div>
        ) : error ? (
          <p className="p-3 text-sm text-destructive">{error}</p>
        ) : hasChildren ? (
          <div className="grid p-1">{children}</div>
        ) : (
          <p className="p-3 text-sm text-muted-foreground">{emptyLabel}</p>
        )}
      </div>
      {truncated && <p className="text-xs text-muted-foreground">Mostrando {shown} resultados; afiná la búsqueda.</p>}
    </div>
  );
}

function TargetRow({
  id,
  checked,
  onToggle,
  primary,
  secondary
}: {
  id: string;
  checked: boolean;
  onToggle: () => void;
  primary: string;
  secondary: string;
}) {
  return (
    <label htmlFor={id} className="flex cursor-pointer items-center gap-2 rounded-md px-2 py-1.5 hover:bg-muted">
      <Checkbox id={id} checked={checked} onCheckedChange={onToggle} />
      <span className="grid">
        <span className="text-sm">{primary}</span>
        {secondary && <span className="text-xs text-muted-foreground">{secondary}</span>}
      </span>
    </label>
  );
}

function SelectedChip({ label, onRemove }: { label: string; onRemove: () => void }) {
  return (
    <span className="inline-flex items-center gap-1">
      <Badge variant="secondary">{label}</Badge>
      <Button type="button" variant="ghost" size="icon-xs" aria-label={`Quitar ${label}`} onClick={onRemove}>
        <XIcon />
      </Button>
    </span>
  );
}
