import type { DeliverySessionState } from "../hooks/useDeliverySession";
import { ActiveSessionPanel } from "./ActiveSessionPanel";
import { SessionCreatePanel } from "./SessionCreatePanel";
import { SessionResumePanel } from "./SessionResumePanel";

type SessionsWorkspaceProps = {
  delivery: DeliverySessionState;
};

export function SessionsWorkspace({ delivery }: SessionsWorkspaceProps) {
  const { examCatalog, sessionForm, activeSession } = delivery;

  return (
    <>
      <div className="left-stack">
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
        <SessionResumePanel
          activeSessions={activeSession.activeSessions}
          resumeAccessCode={activeSession.resumeAccessCode}
          accessCodeError={sessionForm.formErrors.accessCode}
          isBusy={delivery.isBusy}
          onResumeAccessCodeChange={activeSession.setResumeAccessCode}
          onResumeSession={activeSession.resumeSession}
        />
      </div>

      <div className="right-stack">
        {delivery.error && <p className="error-banner">{delivery.error}</p>}
        <ActiveSessionPanel
          progress={activeSession.progress}
          session={activeSession.session}
          sessionLink={activeSession.sessionLink}
        />
      </div>
    </>
  );
}
