"use client";

import type { ExamDocument } from "../../_lib/schema/exam";

interface ExamPreviewProps {
  document: ExamDocument;
}

// Vista previa de solo lectura: muestra el examen como lo vería el estudiante.
export function ExamPreview({ document }: ExamPreviewProps) {
  return (
    <div className="card">
      <div className="card__body stack">
        <header>
          <h2>{document.title || "Examen sin título"}</h2>
          {document.description && <p style={{ color: "var(--text-muted)" }}>{document.description}</p>}
          <p className="resource-card__meta">
            {[document.subject, document.level, document.area].filter(Boolean).join(" · ")}
          </p>
        </header>

        {document.questions.length === 0 && <p style={{ color: "var(--text-muted)" }}>Sin preguntas para previsualizar.</p>}

        <ol className="stack" style={{ paddingLeft: "1.2rem" }}>
          {document.questions.map(question => (
            <li key={question.id} className="stack" style={{ gap: "var(--space-2)" }}>
              <strong>
                {question.prompt || "(sin enunciado)"}
                {question.required && <span className="field-required"> *</span>}
              </strong>
              {question.help && <span className="field__hint">{question.help}</span>}

              {(question.type === "single_choice" || question.type === "multiple_choice") && (
                <div className="stack" style={{ gap: 4 }}>
                  {question.options.map(option => (
                    <label key={option.id} style={{ display: "flex", gap: 8, alignItems: "center" }}>
                      <input
                        type={question.type === "single_choice" ? "radio" : "checkbox"}
                        disabled
                        style={{ width: "auto" }}
                      />
                      {option.label || "(opción vacía)"}
                    </label>
                  ))}
                </div>
              )}

              {question.type === "true_false" && (
                <div className="row">
                  <label style={{ display: "flex", gap: 6 }}>
                    <input type="radio" disabled style={{ width: "auto" }} /> Verdadero
                  </label>
                  <label style={{ display: "flex", gap: 6 }}>
                    <input type="radio" disabled style={{ width: "auto" }} /> Falso
                  </label>
                </div>
              )}

              {question.type === "free_text" && (
                <textarea disabled placeholder="Respuesta del estudiante" maxLength={question.maxLength} />
              )}
            </li>
          ))}
        </ol>
      </div>
    </div>
  );
}
