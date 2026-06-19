import type { LocalExamBlock } from "../../shared/api-types";
import { getBlockKind, parseConfig, parseValidation } from "../examBlocks";
import { QuestionTitle } from "./QuestionTitle";

type ExamBlockProps = {
  block: LocalExamBlock;
  number: number | null;
  value: string;
  isMissing: boolean;
  onChange: (value: string) => void;
};

export function ExamBlock({ block, number, value, isMissing, onChange }: ExamBlockProps) {
  const kind = getBlockKind(block);
  const validation = parseValidation(block);
  const isRequired = validation.required === true;

  if (kind === "text") {
    const config = parseConfig<{ content?: string }>(block);
    return <section className="student-question student-copy">{config.content}</section>;
  }

  if (kind === "image") {
    const config = parseConfig<{ assetId?: string; alt?: string; caption?: string }>(block);
    return (
      <section id={block.id} className="student-question">
        <img
          className="student-image"
          src={`/api/assets/${encodeURIComponent(config.assetId ?? "")}`}
          alt={config.alt ?? config.caption ?? "Imagen del examen"}
        />
        {config.caption && <p className="student-caption">{config.caption}</p>}
      </section>
    );
  }

  if (kind === "multiple_choice") {
    const config = parseConfig<{ question?: string; options?: Array<{ value: string; label: string }> }>(block);
    return (
      <section id={block.id} className="student-question">
        <QuestionTitle number={number} text={config.question ?? ""} required={isRequired} />
        <div className="student-options">
          {(config.options ?? []).map(option => (
            <label className="student-option" key={option.value}>
              <input
                type="radio"
                name={block.id}
                value={option.value}
                checked={value === option.value}
                onChange={() => onChange(option.value)}
              />
              <span>{option.label}</span>
            </label>
          ))}
        </div>
        {isMissing && <p className="student-error">Esta respuesta es obligatoria.</p>}
      </section>
    );
  }

  if (kind === "true_false") {
    const config = parseConfig<{ question?: string }>(block);
    return (
      <section id={block.id} className="student-question">
        <QuestionTitle number={number} text={config.question ?? ""} required={isRequired} />
        <div className="student-options">
          {[
            ["true", "Verdadero"],
            ["false", "Falso"]
          ].map(([optionValue, label]) => (
            <label className="student-option" key={optionValue}>
              <input
                type="radio"
                name={block.id}
                value={optionValue}
                checked={value === optionValue}
                onChange={() => onChange(optionValue)}
              />
              <span>{label}</span>
            </label>
          ))}
        </div>
        {isMissing && <p className="student-error">Esta respuesta es obligatoria.</p>}
      </section>
    );
  }

  const config = parseConfig<{ prompt?: string }>(block);
  return (
    <section id={block.id} className="student-question">
      <QuestionTitle number={number} text={config.prompt ?? ""} required={isRequired} />
      <textarea rows={5} value={value} aria-label="Respuesta" onChange={event => onChange(event.target.value)} />
      {isMissing && <p className="student-error">Esta respuesta es obligatoria.</p>}
    </section>
  );
}
