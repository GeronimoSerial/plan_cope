import { ActionButton, Field, TextInput } from "../../shared/ui";

type SessionEntryPanelProps = {
  sessionCode: string;
  document: string;
  isBusy: boolean;
  error: string;
  recoveryRequired: boolean;
  notFoundPrompt: { message: string; hint: string } | null;
  onSessionCodeChange: (value: string) => void;
  onDocumentChange: (value: string) => void;
  onResolveStudent: () => void;
};

export function SessionEntryPanel({
  sessionCode,
  document,
  isBusy,
  error,
  recoveryRequired,
  notFoundPrompt,
  onSessionCodeChange,
  onDocumentChange,
  onResolveStudent
}: SessionEntryPanelProps) {
  return (
    <section className="student-gate">
      <div className="student-card">
        <p className="eyebrow">Acceso del estudiante</p>
        <h2>Ingresá al examen</h2>
        <p className="student-card-copy">{recoveryRequired
          ? "Tus respuestas pendientes siguen guardadas en esta pestaña. Ingresá el mismo código y tu DNI para recuperar el mismo intento."
          : "Ingresá el código que te indicó el docente. La sesión puede pedirte el DNI."}</p>
        <Field label="Código de sesión">
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
          <span id="student-document-help" className="field-hint">En sesiones nominales lo usamos para buscarte en el padrón.</span>
        </Field>
        <ActionButton disabled={isBusy || !sessionCode.trim()} onClick={onResolveStudent}>
          {isBusy ? "Buscando…" : "Buscar mis datos"}
        </ActionButton>
        {error && <p className="error-banner" role="alert">{error}</p>}
        {notFoundPrompt && (
          <div className="student-card-copy">
            <p>{notFoundPrompt.message}</p>
            <p>{notFoundPrompt.hint}</p>
          </div>
        )}
      </div>
    </section>
  );
}
