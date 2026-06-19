import { ActionButton, Field, TextInput } from "../../shared/ui";

type SessionEntryPanelProps = {
  sessionCode: string;
  isBusy: boolean;
  error: string;
  onSessionCodeChange: (value: string) => void;
  onStartAttempt: () => void;
};

export function SessionEntryPanel({
  sessionCode,
  isBusy,
  error,
  onSessionCodeChange,
  onStartAttempt
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
        <ActionButton disabled={isBusy || !sessionCode.trim()} onClick={onStartAttempt}>
          {isBusy ? "Iniciando..." : "Comenzar"}
        </ActionButton>
        {error && <p className="error-banner">{error}</p>}
      </div>
    </section>
  );
}
