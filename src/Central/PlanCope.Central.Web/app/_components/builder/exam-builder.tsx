"use client";

import { useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import { arrayMove } from "@dnd-kit/sortable";
import { examDocumentSchema, type ExamDocument, type Question, type QuestionType } from "../../_lib/schema/exam";
import { blankQuestion, documentToReplaceRequest } from "../../_lib/schema/mappers";
import { callCentral } from "../../_lib/api/client";
import { getErrorMessage } from "../../_lib/json";
import { Tabs, TabPanel, type TabItem } from "../ui/tabs";
import { Button } from "../ui/button";
import { Banner } from "../ui/banner";
import { QuestionList } from "./question-list";
import { ExamPreview } from "./exam-preview";
import { ExportButton } from "./export-button";
import { PublishPanel } from "./publish-panel";

interface ExamBuilderProps {
  versionId: string;
  status: string;
  initialDocument: ExamDocument;
}

const TABS: TabItem[] = [
  { id: "datos", label: "Datos generales" },
  { id: "preguntas", label: "Preguntas" },
  { id: "preview", label: "Vista previa" },
  { id: "publicar", label: "Publicar" }
];

type Banners = { tone: "info" | "success" | "error"; text: string } | null;

export function ExamBuilder({ versionId, status, initialDocument }: ExamBuilderProps) {
  const router = useRouter();
  const isPublished = status.toLowerCase() === "published";

  const [document, setDocument] = useState<ExamDocument>(initialDocument);
  const [savedSnapshot, setSavedSnapshot] = useState(() => JSON.stringify(initialDocument));
  const [activeTab, setActiveTab] = useState("datos");
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [banner, setBanner] = useState<Banners>(isPublished ? { tone: "info", text: "Versión publicada: solo lectura." } : null);
  const [saving, setSaving] = useState(false);

  const dirty = JSON.stringify(document) !== savedSnapshot;

  const errorsByIndex = useMemo(
    () => (index: number) => {
      const prefix = `questions.${index}.`;
      const result: Record<string, string> = {};
      for (const [key, message] of Object.entries(errors)) {
        if (key.startsWith(prefix)) {
          result[key.slice(prefix.length)] = message;
        }
      }
      return result;
    },
    [errors]
  );

  function patchDocument(patch: Partial<ExamDocument>) {
    setDocument(current => ({ ...current, ...patch }));
  }

  function setQuestions(next: Question[]) {
    setDocument(current => ({ ...current, questions: next }));
  }

  function addQuestion(type: QuestionType) {
    setQuestions([...document.questions, blankQuestion(type)]);
    setActiveTab("preguntas");
  }

  function updateQuestion(index: number, next: Question) {
    setQuestions(document.questions.map((question, i) => (i === index ? next : question)));
  }

  function removeQuestion(index: number) {
    setQuestions(document.questions.filter((_, i) => i !== index));
  }

  function moveQuestion(index: number, direction: -1 | 1) {
    const target = index + direction;
    if (target < 0 || target >= document.questions.length) {
      return;
    }
    setQuestions(arrayMove(document.questions, index, target));
  }

  function reorderQuestion(activeId: string, overId: string) {
    const from = document.questions.findIndex(question => question.id === activeId);
    const to = document.questions.findIndex(question => question.id === overId);
    if (from >= 0 && to >= 0) {
      setQuestions(arrayMove(document.questions, from, to));
    }
  }

  function validate(): ExamDocument | null {
    const result = examDocumentSchema.safeParse(document);
    if (result.success) {
      setErrors({});
      return result.data;
    }
    const map: Record<string, string> = {};
    for (const issue of result.error.issues) {
      const key = issue.path.join(".");
      if (!map[key]) {
        map[key] = issue.message;
      }
    }
    setErrors(map);
    return null;
  }

  async function save() {
    const valid = validate();
    if (!valid) {
      setBanner({ tone: "error", text: "Revisá los campos marcados antes de guardar." });
      return;
    }

    setSaving(true);
    setBanner(null);
    try {
      await callCentral(`exams/versions/${encodeURIComponent(versionId)}/document`, {
        method: "PUT",
        body: JSON.stringify(documentToReplaceRequest(valid))
      });
      setSavedSnapshot(JSON.stringify(document));
      setBanner({ tone: "success", text: "Cambios guardados." });
    } catch (error) {
      setBanner({ tone: "error", text: getErrorMessage(error, "No se pudo guardar el examen.") });
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="stack">
      <div className="row row--between">
        <div>{dirty && !isPublished && <Banner tone="info">Tenés cambios sin guardar.</Banner>}</div>
        <div className="row">
          <ExportButton document={document} onError={text => setBanner({ tone: "error", text })} />
          {!isPublished && (
            <Button onClick={() => void save()} disabled={saving}>
              {saving ? "Guardando…" : "Guardar"}
            </Button>
          )}
        </div>
      </div>

      {banner && <Banner tone={banner.tone}>{banner.text}</Banner>}

      <Tabs tabs={TABS} activeId={activeTab} onChange={setActiveTab} ariaLabel="Secciones del builder" />

      <TabPanel id="datos" active={activeTab === "datos"}>
        <div className="card">
          <div className="card__body">
            <div className="cols-2">
              <div className="field">
                <label htmlFor="meta-code">Código</label>
                <input id="meta-code" value={document.code} readOnly disabled />
              </div>
              <div className="field">
                <label htmlFor="meta-subject">Materia</label>
                <input
                  id="meta-subject"
                  value={document.subject ?? ""}
                  onChange={event => patchDocument({ subject: event.target.value || undefined })}
                  disabled={isPublished}
                />
              </div>
            </div>
            <div className="field">
              <label htmlFor="meta-title">
                Título<span className="field-required" aria-hidden="true">*</span>
              </label>
              <input
                id="meta-title"
                value={document.title}
                onChange={event => patchDocument({ title: event.target.value })}
                aria-invalid={errors.title ? true : undefined}
                disabled={isPublished}
              />
              {errors.title && (
                <span className="field__error" role="alert">
                  {errors.title}
                </span>
              )}
            </div>
            <div className="cols-2">
              <div className="field">
                <label htmlFor="meta-level">Curso / grado</label>
                <input
                  id="meta-level"
                  value={document.level ?? ""}
                  onChange={event => patchDocument({ level: event.target.value || undefined })}
                  disabled={isPublished}
                />
              </div>
              <div className="field">
                <label htmlFor="meta-area">Área</label>
                <input
                  id="meta-area"
                  value={document.area ?? ""}
                  onChange={event => patchDocument({ area: event.target.value || undefined })}
                  disabled={isPublished}
                />
              </div>
            </div>
            <div className="field">
              <label htmlFor="meta-description">Descripción</label>
              <textarea
                id="meta-description"
                value={document.description ?? ""}
                onChange={event => patchDocument({ description: event.target.value || undefined })}
                disabled={isPublished}
              />
            </div>
          </div>
        </div>
      </TabPanel>

      <TabPanel id="preguntas" active={activeTab === "preguntas"}>
        {isPublished ? (
          <ExamPreview document={document} />
        ) : (
          <QuestionList
            questions={document.questions}
            errorsByIndex={errorsByIndex}
            onReorder={reorderQuestion}
            onUpdate={updateQuestion}
            onRemove={removeQuestion}
            onMove={moveQuestion}
            onAdd={addQuestion}
          />
        )}
        {errors.questions && <Banner tone="error">{errors.questions}</Banner>}
      </TabPanel>

      <TabPanel id="preview" active={activeTab === "preview"}>
        <ExamPreview document={document} />
      </TabPanel>

      <TabPanel id="publicar" active={activeTab === "publicar"}>
        {isPublished ? (
          <Banner tone="success">Esta versión ya está publicada.</Banner>
        ) : (
          <PublishPanel
            versionId={versionId}
            defaultSubject={document.subject ?? null}
            hasUnsavedChanges={dirty}
            onPublished={() => {
              setBanner({ tone: "success", text: "Versión publicada correctamente." });
              router.refresh();
            }}
          />
        )}
      </TabPanel>
    </div>
  );
}
