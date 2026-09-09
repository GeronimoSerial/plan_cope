import type { LocalSession, SessionProgress } from "../types";
import { postHostMessage } from "../bridge/nativeBridge";
import { ActionButton, Field, SectionTitle, TextInput } from "../../shared/ui";

type ActiveSessionPanelProps = {
  progress: SessionProgress | null;
  session: LocalSession | null;
  sessionLink: string;
};

export function ActiveSessionPanel({ progress, session, sessionLink }: ActiveSessionPanelProps) {
  if (!session) {
    return null;
  }

  const completion = progress?.completionPercentage ?? 0;
  const submitted = progress?.submittedCount ?? 0;
  const expected = progress?.expectedStudentCount ?? session?.expectedStudentCount ?? 0;
  const inProgress = progress?.inProgressCount ?? 0;

  return (
    <aside className="panel session-panel">
      <SectionTitle
        title="Sesión activa"
        description="Compartí el código o el enlace con los estudiantes."
      />

      <div className="session-code session-code-active">
        <span>Código de sesión</span>
        <strong>{session.accessCode}</strong>
      </div>

      <Field label="Enlace para estudiantes">
        <TextInput value={sessionLink} readOnly placeholder="Se genera al crear la sesión" onChange={() => undefined} />
      </Field>

      <div className="progress-summary">
        <div>
          <span>Entregados</span>
          <strong>
            {submitted} / {expected}
          </strong>
        </div>
        <div>
          <span>En curso</span>
          <strong>{inProgress}</strong>
        </div>
      </div>

      <div
        className="progress-bar"
        role="progressbar"
        aria-label="Progreso de la sesión"
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={completion}
        aria-valuetext={`${completion}% completado`}
      >
        <span style={{ width: `${completion}%` }} />
      </div>
      <p className="progress-label">{completion}% completado</p>

      <ActionButton
        variant="secondary"
        onClick={() => postHostMessage({ type: "host:openStudentView", accessCode: session.accessCode })}
      >
        Abrir vista del estudiante
      </ActionButton>
    </aside>
  );
}
