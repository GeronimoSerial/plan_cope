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
          courses={examCatalog.courses}
          divisions={examCatalog.divisions}
          exams={examCatalog.filteredExams}
          form={sessionForm.form}
          formErrors={sessionForm.formErrors}
          schoolName={sessionForm.schoolName}
          selectedCourse={examCatalog.selectedCourse}
          selectedDivision={examCatalog.selectedDivision}
          selectedExamId={examCatalog.selectedExamId}
          isBusy={delivery.isBusy}
          isLoadingExams={examCatalog.isLoadingExams}
          onClassroomCodeChange={value => sessionForm.updateForm("classroomCode", value)}
          onCourseChange={examCatalog.setSelectedCourse}
          onCreateSession={delivery.createSession}
          onDivisionChange={examCatalog.setSelectedDivision}
          onExpectedStudentCountChange={value => sessionForm.updateForm("expectedStudentCount", value)}
          onOperatorNameChange={value => sessionForm.updateForm("operatorName", value)}
          onRefreshExams={() => examCatalog.loadExams()}
          onSelectedExamChange={examCatalog.setSelectedExamId}
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
