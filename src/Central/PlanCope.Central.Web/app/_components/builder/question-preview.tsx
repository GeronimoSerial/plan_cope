import Image from "next/image";
import type { Question } from "../../_lib/schema/exam";

interface QuestionPreviewProps {
  question: Question;
  versionId: string;
  number?: number;
}

/** Read-only student-facing rendering. Correct-answer metadata is intentionally ignored. */
export function QuestionPreview({ question, versionId, number }: QuestionPreviewProps) {
  const prompt = question.prompt?.trim() || "(sin enunciado)";
  const options = question.type === "single_choice" || question.type === "multiple_choice"
    ? question.options
    : question.type === "true_false"
      ? [
          { id: "true", label: "Verdadero" },
          { id: "false", label: "Falso" }
        ]
      : [];
  // Local's ExamBlock currently renders the published choice block with radios.
  const inputType = "radio";

  return (
    <section className="grid gap-3 rounded-lg border bg-card p-4" aria-label={number ? `Pregunta ${number}` : "Pregunta"}>
      <h3 className="m-0 text-base font-semibold leading-relaxed">
        {number !== undefined && <span className="mr-2 inline-flex min-h-7 min-w-7 items-center justify-center rounded-sm bg-primary px-1.5 text-sm font-bold text-primary-foreground">{number}</span>}
        {prompt}
        {question.required && <span className="text-destructive" aria-label="pregunta obligatoria"> *</span>}
      </h3>

      {question.imageAssetId && (
        <Image
          src={`/api/central/exams/versions/${encodeURIComponent(versionId)}/assets/${encodeURIComponent(question.imageAssetId)}`}
          alt={question.prompt?.trim() || "Imagen de la pregunta"}
          width={1280}
          height={800}
          unoptimized
          className="my-1 max-h-[min(50vh,420px)] max-w-full rounded-md object-contain"
        />
      )}

      {question.help?.trim() && (
        <p className="m-0 text-sm leading-relaxed text-muted-foreground">{question.help}</p>
      )}

      {options.length > 0 && (
        <fieldset disabled className="grid min-w-0 gap-2 border-0 p-0" aria-label="Opciones de respuesta">
          <legend className="sr-only">Opciones de respuesta</legend>
          {options.map(option => (
            <label
              key={option.id}
              className="flex min-h-12 items-center gap-3 rounded-md border bg-background px-3 py-2 leading-relaxed text-foreground"
            >
              <input
                type={inputType}
                name={`preview-${question.id}`}
                value={option.id}
                checked={false}
                disabled
                readOnly
                className="size-5 shrink-0 accent-primary"
                aria-label={option.label || "Opción vacía"}
              />
              <span>{option.label || "(opción vacía)"}</span>
            </label>
          ))}
        </fieldset>
      )}
      {(question.type === "single_choice" || question.type === "multiple_choice") && options.length === 0 && (
        <p className="m-0 text-sm text-muted-foreground">Sin opciones cargadas.</p>
      )}
    </section>
  );
}
