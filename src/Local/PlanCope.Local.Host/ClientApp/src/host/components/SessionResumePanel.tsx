import type { LocalSession } from "../types";
import { ActionButton, Field, TextInput } from "../../shared/ui";

type SessionResumePanelProps = {
  activeSessions: LocalSession[];
  resumeAccessCode: string;
  accessCodeError?: string;
  isBusy: boolean;
  onResumeAccessCodeChange: (value: string) => void;
  onResumeSession: (accessCode?: string) => void;
};

export function SessionResumePanel({
  activeSessions,
  resumeAccessCode,
  accessCodeError,
  isBusy,
  onResumeAccessCodeChange,
  onResumeSession
}: SessionResumePanelProps) {
  return (
    <details className="resume-details">
      <summary>Retomar sesión</summary>
      <div className="resume-details-content">
        <p className="panel-description">Usá un código activo para volver a una sesión en este equipo.</p>
        {activeSessions.length > 0 && (
          <div className="session-list" aria-label="Sesiones recientes">
            {activeSessions.slice(0, 4).map(session => (
              <button
                key={session.id}
                type="button"
                aria-label={`Reabrir sesión ${session.accessCode}`}
                onClick={() => onResumeSession(session.accessCode)}
              >
                <strong>{session.accessCode}</strong>
                <span>
                  {session.schoolCode} · {session.classroomCode ?? "Sin aula"} · {session.status}
                </span>
              </button>
            ))}
          </div>
        )}
        <Field label="Código de sesión" error={accessCodeError}>
          <TextInput value={resumeAccessCode} onChange={onResumeAccessCodeChange} />
        </Field>
        <ActionButton variant="secondary" disabled={isBusy} onClick={() => onResumeSession()}>
          Reabrir sesión
        </ActionButton>
      </div>
    </details>
  );
}
