import { useMemo, useState } from "react";
import type { ReactNode } from "react";
import type { LocalExamBlock } from "../types";
import { getBlockKind, hasAnswer, isAnswerBlock, parseConfig, parseValidation } from "./examBlocks";
import { StudentApi } from "./studentApi";

type AnswerMap = Record<string, string>;

export function StudentExamPage() {
  const api = useMemo(() => new StudentApi(), []);
  const initialCode = decodeURIComponent(window.location.pathname.split("/").filter(Boolean)[1] ?? "");
  const [sessionCode, setSessionCode] = useState(initialCode);
  const [attemptId, setAttemptId] = useState<string | null>(null);
  const [blocks, setBlocks] = useState<LocalExamBlock[]>([]);
  const [answers, setAnswers] = useState<AnswerMap>({});
  const [missingRequired, setMissingRequired] = useState<Set<string>>(new Set());
  const [confirmationCode, setConfirmationCode] = useState<string | null>(null);
  const [status, setStatus] = useState("");
  const [error, setError] = useState("");
  const [isBusy, setIsBusy] = useState(false);

  async function startAttempt() {
    if (!sessionCode.trim()) {
      setError("Completa el codigo de examen.");
      return;
    }

    await runBusy(async () => {
      const response = await api.startAttempt(sessionCode.trim());
      setAttemptId(response.attempt.id);
      setBlocks(response.blocks);
      setAnswers({});
      setMissingRequired(new Set());
      setConfirmationCode(null);
      setStatus("");
    });
  }

  async function saveAnswers() {
    if (!attemptId || !validateRequired()) {
      return false;
    }

    return runBusy(async () => {
      await api.saveAnswers(attemptId, collectAnswers(blocks, answers));
      setStatus("Respuestas guardadas.");
    });
  }

  async function submitAttempt() {
    if (!attemptId || !(await saveAnswers())) {
      return;
    }

    await runBusy(async () => {
      const response = await api.submitAttempt(attemptId);
      setConfirmationCode(response.confirmationCode);
      setStatus("");
    });
  }

  function validateRequired() {
    const missing = new Set(
      blocks
        .filter(isAnswerBlock)
        .filter(block => parseValidation(block).required === true)
        .filter(block => !hasAnswer(answers[block.id]))
        .map(block => block.id)
    );

    setMissingRequired(missing);
    if (missing.size > 0) {
      setError("Completa las respuestas obligatorias antes de continuar.");
      return false;
    }

    setError("");
    return true;
  }

  async function runBusy(action: () => Promise<void>) {
    setIsBusy(true);
    setError("");
    try {
      await action();
      return true;
    } catch (exception) {
      setError(exception instanceof Error ? exception.message : "No se pudo completar la operacion.");
      return false;
    } finally {
      setIsBusy(false);
    }
  }

  if (confirmationCode) {
    return (
      <StudentShell>
        <section className="student-panel">
          <h2>Examen enviado</h2>
          <p>Codigo de confirmacion:</p>
          <strong className="confirmation-code">{confirmationCode}</strong>
          <p>Entrega registrada en este equipo. No cierres esta pantalla hasta que el docente lo indique.</p>
        </section>
      </StudentShell>
    );
  }

  return (
    <StudentShell>
      {!attemptId ? (
        <section className="student-panel">
          <h2>Ingresar al examen</h2>
          <label className="student-field">
            Codigo de examen
            <input value={sessionCode} onChange={event => setSessionCode(event.target.value)} autoComplete="off" />
          </label>
          <button className="student-button" disabled={isBusy} onClick={startAttempt}>
            {isBusy ? "Iniciando..." : "Comenzar"}
          </button>
          {error && <p className="student-error">{error}</p>}
        </section>
      ) : (
        <section className="student-panel">
          <h2>Responder examen</h2>
          <div className="student-questions">
            {blocks.map((block, index) => (
              <ExamBlock
                key={block.id}
                block={block}
                number={questionNumberFor(blocks, index)}
                value={answers[block.id] ?? ""}
                isMissing={missingRequired.has(block.id)}
                onChange={value => setAnswers(current => ({ ...current, [block.id]: value }))}
              />
            ))}
          </div>
          <div className="student-actions">
            <button className="student-button student-button-secondary" disabled={isBusy} onClick={saveAnswers}>
              Guardar respuestas
            </button>
            <button className="student-button" disabled={isBusy} onClick={submitAttempt}>
              Enviar examen
            </button>
          </div>
          {status && <p className="student-status">{status}</p>}
          {error && <p className="student-error">{error}</p>}
        </section>
      )}
    </StudentShell>
  );
}

function StudentShell({ children }: { children: ReactNode }) {
  return (
    <main className="student-page">
      <header className="student-header">
        <h1>Plan Cope</h1>
        <p>Toma local de examen. Funciona dentro de la red de la escuela.</p>
      </header>
      {children}
    </main>
  );
}

function ExamBlock(props: {
  block: LocalExamBlock;
  number: number | null;
  value: string;
  isMissing: boolean;
  onChange: (value: string) => void;
}) {
  const kind = getBlockKind(props.block);
  const validation = parseValidation(props.block);
  const requiredMark = validation.required === true ? " *" : "";

  if (kind === "text") {
    const config = parseConfig<{ content?: string }>(props.block);
    return <section className="student-question student-copy">{config.content}</section>;
  }

  if (kind === "image") {
    const config = parseConfig<{ assetId?: string; alt?: string; caption?: string }>(props.block);
    return (
      <section className="student-question">
        <img className="student-image" src={`/api/assets/${encodeURIComponent(config.assetId ?? "")}`} alt={config.alt ?? config.caption ?? "Imagen del examen"} />
        {config.caption && <p className="student-caption">{config.caption}</p>}
      </section>
    );
  }

  if (kind === "multiple_choice") {
    const config = parseConfig<{ question?: string; options?: Array<{ value: string; label: string }> }>(props.block);
    return (
      <section className="student-question">
        <QuestionTitle number={props.number} text={`${config.question ?? ""}${requiredMark}`} />
        <div className="student-options">
          {(config.options ?? []).map(option => (
            <label className="student-option" key={option.value}>
              <input
                type="radio"
                name={props.block.id}
                value={option.value}
                checked={props.value === option.value}
                onChange={() => props.onChange(option.value)}
              />
              <span>{option.label}</span>
            </label>
          ))}
        </div>
        {props.isMissing && <p className="student-error">Esta respuesta es obligatoria.</p>}
      </section>
    );
  }

  if (kind === "true_false") {
    const config = parseConfig<{ question?: string }>(props.block);
    return (
      <section className="student-question">
        <QuestionTitle number={props.number} text={`${config.question ?? ""}${requiredMark}`} />
        <div className="student-options">
          {[
            ["true", "Verdadero"],
            ["false", "Falso"]
          ].map(([value, label]) => (
            <label className="student-option" key={value}>
              <input type="radio" name={props.block.id} value={value} checked={props.value === value} onChange={() => props.onChange(value)} />
              <span>{label}</span>
            </label>
          ))}
        </div>
        {props.isMissing && <p className="student-error">Esta respuesta es obligatoria.</p>}
      </section>
    );
  }

  const config = parseConfig<{ prompt?: string }>(props.block);
  return (
    <section className="student-question">
      <QuestionTitle number={props.number} text={`${config.prompt ?? ""}${requiredMark}`} />
      <textarea rows={5} value={props.value} aria-label="Respuesta" onChange={event => props.onChange(event.target.value)} />
      {props.isMissing && <p className="student-error">Esta respuesta es obligatoria.</p>}
    </section>
  );
}

function QuestionTitle({ number, text }: { number: number | null; text: string }) {
  return (
    <h3>
      {number !== null && <span className="student-question-number">{number}</span>}
      {text}
    </h3>
  );
}

function questionNumberFor(blocks: LocalExamBlock[], index: number) {
  if (!isAnswerBlock(blocks[index])) {
    return null;
  }

  return blocks.slice(0, index + 1).filter(isAnswerBlock).length;
}

function collectAnswers(blocks: LocalExamBlock[], answers: AnswerMap) {
  return blocks.filter(isAnswerBlock).map(block => ({
    blockId: block.id,
    answer: answers[block.id] ?? null
  }));
}
