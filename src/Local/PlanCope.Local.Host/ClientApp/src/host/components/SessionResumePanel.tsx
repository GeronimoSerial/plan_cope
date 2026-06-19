import type { LocalSession } from "../types";
import { ActionButton, Field, SectionTitle, TextInput } from "../../shared/ui";

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
    <section className="panel">
      <SectionTitle
        title="Retomar una sesion"
        description="Usa un codigo activo para reabrir una toma anterior en este equipo."
      />
      {activeSessions.length > 0 && (
        <div className="session-list">
          {activeSessions.slice(0, 4).map(session => (
            <button key={session.id} type="button" onClick={() => onResumeSession(session.accessCode)}>
              <strong>{session.accessCode}</strong>
              <span>
                {session.schoolCode} - {session.classroomCode ?? "Sin aula"} - {session.status}
              </span>
            </button>
          ))}
        </div>
      )}
      <Field label="Codigo de sesion" error={accessCodeError}>
        <TextInput value={resumeAccessCode} onChange={onResumeAccessCodeChange} />
      </Field>
      <ActionButton variant="secondary" disabled={isBusy} onClick={() => onResumeSession()}>
        Reabrir por codigo
      </ActionButton>
    </section>
  );
}
