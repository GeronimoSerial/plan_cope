"use client";

import { useId } from "react";
import { questionTypes, questionTypeLabels, type Question, type QuestionType, type ExamOption } from "../../_lib/schema/exam";
import { blankQuestion, newId } from "../../_lib/schema/mappers";
import { Button } from "../ui/button";

interface QuestionEditorProps {
  question: Question;
  errors: Record<string, string>;
  onChange: (next: Question) => void;
}

export function QuestionEditor({ question, errors, onChange }: QuestionEditorProps) {
  const uid = useId();

  function patchCommon(patch: Partial<Pick<Question, "prompt" | "help" | "required" | "score">>) {
    onChange({ ...question, ...patch } as Question);
  }

  function changeType(type: QuestionType) {
    if (type === question.type) {
      return;
    }
    // Conserva enunciado/ayuda/puntaje/obligatorio; reinicia lo especifico del tipo.
    const fresh = blankQuestion(type);
    onChange({
      ...fresh,
      id: question.id,
      prompt: question.prompt,
      help: question.help,
      required: question.required,
      score: question.score
    } as Question);
  }

  return (
    <div className="stack">
      <div className="field">
        <label htmlFor={`${uid}-type`}>Tipo de pregunta</label>
        <select
          id={`${uid}-type`}
          value={question.type}
          onChange={event => changeType(event.target.value as QuestionType)}
        >
          {questionTypes.map(type => (
            <option key={type} value={type}>
              {questionTypeLabels[type]}
            </option>
          ))}
        </select>
      </div>

      <div className="field">
        <label htmlFor={`${uid}-prompt`}>
          Enunciado<span className="field-required" aria-hidden="true">*</span>
        </label>
        <textarea
          id={`${uid}-prompt`}
          value={question.prompt}
          onChange={event => patchCommon({ prompt: event.target.value })}
          aria-invalid={errors.prompt ? true : undefined}
          aria-describedby={errors.prompt ? `${uid}-prompt-error` : undefined}
          placeholder="Escribí la pregunta tal como la verá el estudiante."
        />
        {errors.prompt && (
          <span className="field__error" id={`${uid}-prompt-error`} role="alert">
            {errors.prompt}
          </span>
        )}
      </div>

      <div className="cols-2">
        <div className="field">
          <label htmlFor={`${uid}-help`}>Texto de ayuda</label>
          <input
            id={`${uid}-help`}
            value={question.help ?? ""}
            onChange={event => patchCommon({ help: event.target.value || undefined })}
            placeholder="Aclaración opcional."
          />
        </div>
        <div className="field">
          <label htmlFor={`${uid}-score`}>Puntaje</label>
          <input
            id={`${uid}-score`}
            type="number"
            min={0}
            step={1}
            value={question.score}
            onChange={event => patchCommon({ score: Number(event.target.value) })}
          />
        </div>
      </div>

      <label style={{ display: "flex", alignItems: "center", gap: "var(--space-2)", fontWeight: 600 }}>
        <input
          type="checkbox"
          checked={question.required}
          onChange={event => patchCommon({ required: event.target.checked })}
          style={{ width: "auto" }}
        />
        Respuesta obligatoria
      </label>

      {(question.type === "single_choice" || question.type === "multiple_choice") && (
        <ChoiceEditor question={question} errors={errors} onChange={onChange} />
      )}

      {question.type === "true_false" && (
        <fieldset style={{ border: "1px solid var(--line)", borderRadius: "var(--radius-sm)", padding: "var(--space-3)" }}>
          <legend style={{ fontWeight: 700, fontSize: 13 }}>Respuesta correcta</legend>
          <div className="row">
            {[true, false].map(value => (
              <label key={String(value)} style={{ display: "flex", alignItems: "center", gap: "var(--space-2)" }}>
                <input
                  type="radio"
                  name={`${uid}-tf`}
                  checked={question.correctAnswer === value}
                  onChange={() => onChange({ ...question, correctAnswer: value })}
                  style={{ width: "auto" }}
                />
                {value ? "Verdadero" : "Falso"}
              </label>
            ))}
          </div>
        </fieldset>
      )}

      {question.type === "free_text" && (
        <div className="cols-2">
          <div className="field">
            <label htmlFor={`${uid}-sample`}>Respuesta modelo (opcional)</label>
            <input
              id={`${uid}-sample`}
              value={question.sampleAnswer ?? ""}
              onChange={event => onChange({ ...question, sampleAnswer: event.target.value || undefined })}
              placeholder="Referencia para corregir."
            />
          </div>
          <div className="field">
            <label htmlFor={`${uid}-max`}>Largo máximo (caracteres)</label>
            <input
              id={`${uid}-max`}
              type="number"
              min={1}
              value={question.maxLength ?? ""}
              onChange={event =>
                onChange({ ...question, maxLength: event.target.value ? Number(event.target.value) : undefined })
              }
            />
          </div>
        </div>
      )}
    </div>
  );
}

function ChoiceEditor({
  question,
  errors,
  onChange
}: {
  question: Extract<Question, { type: "single_choice" | "multiple_choice" }>;
  errors: Record<string, string>;
  onChange: (next: Question) => void;
}) {
  const single = question.type === "single_choice";

  function updateOptions(options: ExamOption[]) {
    onChange({ ...question, options });
  }

  function setLabel(id: string, label: string) {
    updateOptions(question.options.map(option => (option.id === id ? { ...option, label } : option)));
  }

  function toggleCorrect(id: string) {
    if (single) {
      updateOptions(question.options.map(option => ({ ...option, isCorrect: option.id === id })));
    } else {
      updateOptions(question.options.map(option => (option.id === id ? { ...option, isCorrect: !option.isCorrect } : option)));
    }
  }

  function addOption() {
    updateOptions([...question.options, { id: newId(), label: "", isCorrect: false }]);
  }

  function removeOption(id: string) {
    if (question.options.length <= 2) {
      return;
    }
    updateOptions(question.options.filter(option => option.id !== id));
  }

  return (
    <fieldset style={{ border: "1px solid var(--line)", borderRadius: "var(--radius-sm)", padding: "var(--space-3)" }}>
      <legend style={{ fontWeight: 700, fontSize: 13 }}>
        Opciones {single ? "(marcá la correcta)" : "(marcá todas las correctas)"}
      </legend>
      {question.options.map((option, index) => (
        <div className="option-row" key={option.id}>
          <label style={{ display: "flex", alignItems: "center", gap: 4 }}>
            <input
              type={single ? "radio" : "checkbox"}
              name={`correct-${question.id}`}
              checked={option.isCorrect}
              onChange={() => toggleCorrect(option.id)}
              style={{ width: "auto" }}
              aria-label={`Marcar opción ${index + 1} como correcta`}
            />
          </label>
          <input
            type="text"
            value={option.label}
            onChange={event => setLabel(option.id, event.target.value)}
            placeholder={`Opción ${index + 1}`}
            aria-label={`Texto de la opción ${index + 1}`}
          />
          <Button
            variant="ghost"
            size="sm"
            onClick={() => removeOption(option.id)}
            disabled={question.options.length <= 2}
            aria-label={`Eliminar opción ${index + 1}`}
          >
            ✕
          </Button>
        </div>
      ))}
      {errors.options && (
        <span className="field__error" role="alert">
          {errors.options}
        </span>
      )}
      <div style={{ marginTop: "var(--space-2)" }}>
        <Button variant="secondary" size="sm" onClick={addOption}>
          + Agregar opción
        </Button>
      </div>
    </fieldset>
  );
}
