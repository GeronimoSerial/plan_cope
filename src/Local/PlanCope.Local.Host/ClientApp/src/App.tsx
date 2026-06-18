import { useState } from "react";
import { ActiveSessionPanel } from "./components/ActiveSessionPanel";
import { AppShell } from "./components/AppShell";
import { SchoolGate } from "./components/SchoolGate";
import { SessionFormPanel } from "./components/SessionFormPanel";
import { useDeliverySession } from "./hooks/useDeliverySession";
import { useHostContext } from "./hooks/useHostContext";

export function App() {
  const hostContext = useHostContext();
  const sessionState = useDeliverySession(hostContext);
  const [isSchoolConfirmed, setIsSchoolConfirmed] = useState(false);

  if (!isSchoolConfirmed) {
    return (
      <SchoolGate
        cue={sessionState.form.cue}
        schoolName={sessionState.schoolName}
        onCueChange={value => sessionState.updateForm("cue", value)}
        onContinue={() => setIsSchoolConfirmed(true)}
      />
    );
  }

  return (
    <AppShell status={sessionState.status}>
      <SessionFormPanel
        classroomCode={sessionState.form.classroomCode}
        activeSessions={sessionState.activeSessions}
        courses={sessionState.courses}
        divisions={sessionState.divisions}
        exams={sessionState.filteredExams}
        formErrors={sessionState.formErrors}
        expectedStudentCount={sessionState.form.expectedStudentCount}
        isBusy={sessionState.isBusy}
        isLoadingExams={sessionState.isLoadingExams}
        operatorName={sessionState.form.operatorName}
        resumeAccessCode={sessionState.resumeAccessCode}
        schoolCode={sessionState.form.cue}
        schoolName={sessionState.schoolName}
        selectedCourse={sessionState.selectedCourse}
        selectedDivision={sessionState.selectedDivision}
        selectedExamId={sessionState.selectedExamId}
        onClassroomCodeChange={value => sessionState.updateForm("classroomCode", value)}
        onCourseChange={sessionState.setSelectedCourse}
        onCreateSession={sessionState.createSession}
        onDivisionChange={sessionState.setSelectedDivision}
        onExpectedStudentCountChange={value => sessionState.updateForm("expectedStudentCount", value)}
        onOperatorNameChange={value => sessionState.updateForm("operatorName", value)}
        onRefreshExams={() => sessionState.loadExams()}
        onResumeAccessCodeChange={sessionState.setResumeAccessCode}
        onResumeSession={sessionState.resumeSession}
        onSelectedExamChange={sessionState.setSelectedExamId}
      />

      <div className="right-stack">
        {sessionState.error && <p className="error-banner">{sessionState.error}</p>}
        <ActiveSessionPanel
          progress={sessionState.progress}
          session={sessionState.session}
          sessionLink={sessionState.sessionLink}
        />
      </div>
    </AppShell>
  );
}
