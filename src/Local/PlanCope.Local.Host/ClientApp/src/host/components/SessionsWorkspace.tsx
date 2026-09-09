import type { DeliverySessionState } from "../hooks/useDeliverySession";
import { ActiveSessionPanel } from "./ActiveSessionPanel";
import { SessionCreatePanel } from "./SessionCreatePanel";
import { SessionResumePanel } from "./SessionResumePanel";

type SessionsWorkspaceProps = {
  delivery: DeliverySessionState;
};

export function SessionsWorkspace({ delivery }: SessionsWorkspaceProps) {
  const { examCatalog, sessionForm, activeSession } = delivery;
  const hasActiveSession = Boolean(activeSession.session);

  const createPanel = (
    <SessionCreatePanel
      exams={examCatalog.exams}
      formErrors={sessionForm.formErrors}
      selectedExamId={examCatalog.selectedExamId}
      isBusy={delivery.isBusy}
      isLoadingExams={examCatalog.isLoadingExams}
      onCreateSession={delivery.createSession}
      onRefreshExams={() => examCatalog.loadExams()}
      onSelectedExamChange={examCatalog.setSelectedExamId}
      roster={delivery.roster}
    />
  );

  const resumePanel = (
    <SessionResumePanel
      activeSessions={activeSession.activeSessions}
      resumeAccessCode={activeSession.resumeAccessCode}
      accessCodeError={sessionForm.formErrors.accessCode}
      isBusy={delivery.isBusy}
      onResumeAccessCodeChange={activeSession.setResumeAccessCode}
      onResumeSession={activeSession.resumeSession}
    />
  );

  const activePanel = (
    <ActiveSessionPanel
      progress={activeSession.progress}
      session={activeSession.session}
      sessionLink={activeSession.sessionLink}
    />
  );

  return (
    <>
      {delivery.error && <p className="error-banner workspace-error" role="alert">{delivery.error}</p>}

      {hasActiveSession ? (
        <>
          <div className="active-session-focus">{activePanel}</div>
          <div className="session-tools">
            <details className="secondary-action">
              <summary>Nueva sesión</summary>
              {createPanel}
            </details>
            {resumePanel}
          </div>
        </>
      ) : (
        <>
          <div className="left-stack">{createPanel}</div>
          <div className="right-stack session-secondary">{resumePanel}</div>
        </>
      )}
    </>
  );
}
