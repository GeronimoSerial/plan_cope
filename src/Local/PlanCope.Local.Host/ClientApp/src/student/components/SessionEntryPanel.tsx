import { ActionButton, Field, TextInput } from "../../shared/ui";

type SessionEntryPanelProps = {
  sessionCode: string;
  document: string;
  isBusy: boolean;
  error: string;
  onSessionCodeChange: (value: string) => void;
  onDocumentChange: (value: string) => void;
  onResolveStudent: () => void;
};

export function SessionEntryPanel({
  sessionCode,
  document,
  isBusy,
  error,
  onSessionCodeChange,
  onDocumentChange,
  onResolveStudent
}: SessionEntryPanelProps) {
  return (
    <section className="student-gate">
      <div className="student-card">
        <p className="eyebrow">Acceso del alumno</p>
        <h2>Ingresar al examen</h2>
        <p className="student-card-copy">
          Escribe el codigo que te indico el docente. Solo funciona dentro de la red de la escuela.
        </p>
        <Field label="Codigo de examen">
          <TextInput
            value={sessionCode}
            placeholder="Ej. ABC-123"
            onChange={value => onSessionCodeChange(value.toUpperCase())}
          />
        </Field>
        <Field label="DNI">
          <input
            className="control"
            type="text"
            inputMode="numeric"
            autoComplete="off"
            pattern="[0-9 .-]*"
            value={document}
            placeholder="Ej. 12.345.678"
            aria-describedby="student-document-help"
            onChange={event => onDocumentChange(event.target.value)}
          />
          <span id="student-document-help" className="field-hint">Usamos tu DNI sólo para buscarte en el padrón de esta sección.</span>
        </Field>
        <ActionButton disabled={isBusy || !sessionCode.trim() || !document.trim()} onClick={onResolveStudent}>
          {isBusy ? "Buscando..." : "Buscar mis datos"}
        </ActionButton>
        {error && <p className="error-banner" role="alert">{error}</p>}
      </div>
    </section>
  );
}
