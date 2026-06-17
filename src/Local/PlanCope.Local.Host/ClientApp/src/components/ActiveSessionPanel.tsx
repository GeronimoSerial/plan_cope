import type { LocalSession, SessionProgress } from "../types";
import { postHostMessage } from "../hooks/useHostContext";
import { ActionButton, Field, SectionTitle, TextInput } from "./ui";

type ActiveSessionPanelProps = {
  progress: SessionProgress | null;
  session: LocalSession | null;
  sessionLink: string;
};

export function ActiveSessionPanel({ progress, session, sessionLink }: ActiveSessionPanelProps) {
  const completion = progress?.completionPercentage ?? 0;
  const submitted = progress?.submittedCount ?? 0;
  const expected = progress?.expectedStudentCount ?? session?.expectedStudentCount ?? 0;
  const inProgress = progress?.inProgressCount ?? 0;

  return (
    <aside className="panel session-panel">
      <SectionTitle
        title="Sesion activa"
        description="Comparte el codigo o el enlace de red local cuando la sesion este creada."
      />

      <div className={`session-code ${session ? "session-code-active" : ""}`}>
        <span>Codigo de sesion</span>
        <strong>{session?.accessCode ?? "-----"}</strong>
      </div>

      <Field label="Enlace para alumnos">
        <TextInput value={sessionLink} readOnly placeholder="Se genera al crear la sesion" onChange={() => undefined} />
      </Field>

      <div className="progress-summary">
        <div>
          <span>Completados</span>
          <strong>
            {submitted} / {expected}
          </strong>
        </div>
        <div>
          <span>En curso</span>
          <strong>{inProgress}</strong>
        </div>
      </div>

      <div className="progress-bar" aria-label={`Progreso ${completion}%`}>
        <span style={{ width: `${completion}%` }} />
      </div>
      <p className="progress-label">{completion}% completado</p>

      <ActionButton
        variant="secondary"
        disabled={!session}
        onClick={() => postHostMessage({ type: "host:openStudentView", accessCode: session?.accessCode })}
      >
        Abrir vista alumno
      </ActionButton>
    </aside>
  );
}
