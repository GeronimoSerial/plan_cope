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
    <section className="student-panel">
      <h2>Ingresar al examen</h2>
      <Field label="Codigo de examen">
        <TextInput value={sessionCode} onChange={onSessionCodeChange} />
      </Field>
      <ActionButton disabled={isBusy} onClick={onStartAttempt}>
        {isBusy ? "Iniciando..." : "Comenzar"}
      </ActionButton>
      {error && <p className="student-error">{error}</p>}
    </section>
  );
}
